#include "api.h"

#include <atomic>
#include <csignal>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <memory>
#include <mutex>
#include <random>
#include <sstream>

#include <httplib.h>
#include <nlohmann/json.hpp>

#include "session.h"

namespace cplay {

namespace {

using json = nlohmann::json;

httplib::Server *g_server = nullptr;

void onSignal(int) {
    if (g_server) {
        g_server->stop();
    }
}

bool constantTimeEquals(const std::string &a, const std::string &b) {
    if (a.size() != b.size()) {
        return false;
    }
    unsigned char diff = 0;
    for (size_t i = 0; i < a.size(); i++) {
        diff |= static_cast<unsigned char>(a[i] ^ b[i]);
    }
    return diff == 0;
}

std::string randomId() {
    static std::random_device device;
    std::uniform_int_distribution<int> digit(0, 15);
    std::string id;
    for (int i = 0; i < 16; i++) {
        id += "0123456789abcdef"[digit(device)];
    }
    return id;
}

void error(httplib::Response &res, int status, const std::string &message) {
    res.status = status;
    res.set_content(json{{"message", message}}.dump(), "application/json");
}

// The canonical path when it is inside folder, else empty.
std::string inside(const std::string &folder, const std::string &path) {
    std::error_code ec;
    auto root = std::filesystem::weakly_canonical(folder, ec);
    auto target = std::filesystem::weakly_canonical(path, ec);
    if (ec || root.empty()) {
        return {};
    }
    auto rel = target.lexically_relative(root);
    if (rel.empty() || rel.native().rfind("..", 0) == 0 || rel.is_absolute()) {
        return {};
    }
    return target.string();
}

std::string readText(const std::string &path) {
    std::ifstream in(path, std::ios::binary);
    std::stringstream buffer;
    buffer << in.rdbuf();
    return buffer.str();
}

} // namespace

int runApi(const ApiOptions &options) {
    httplib::Server server;
    std::mutex mutex;
    std::shared_ptr<Session> session; // one game at a time: one GPU, one encoder

    auto current = [&](const std::string &id) -> std::shared_ptr<Session> {
        std::lock_guard<std::mutex> lock(mutex);
        return session && session->id() == id ? session : nullptr;
    };

    server.set_pre_routing_handler([&](const httplib::Request &req, httplib::Response &res) {
        if (!options.devPage.empty() && req.method == "GET" && req.path == "/dev") {
            return httplib::Server::HandlerResponse::Unhandled;
        }
        auto key = req.get_header_value("X-Api-Key");
        if (key.empty()) {
            auto auth = req.get_header_value("Authorization");
            if (auth.rfind("Bearer ", 0) == 0) {
                key = auth.substr(7);
            }
        }
        if (options.apiKey.empty() || !constantTimeEquals(key, options.apiKey)) {
            error(res, 401, "A valid API key is required.");
            return httplib::Server::HandlerResponse::Handled;
        }
        return httplib::Server::HandlerResponse::Unhandled;
    });

    server.Get("/health", [&](const httplib::Request &, httplib::Response &res) {
        std::lock_guard<std::mutex> lock(mutex);
        json body{{"ok", true}, {"version", CINNABARR_PLAY_VERSION}, {"session", session ? json(session->id()) : json(nullptr)}};
        res.set_content(body.dump(), "application/json");
    });

    // { core, game, system?, save?, encoder?, bitrateKbps? } -> { id, sdp }. Replaces any game already running.
    server.Post("/sessions", [&](const httplib::Request &req, httplib::Response &res) {
        json body = json::parse(req.body, nullptr, false);
        if (body.is_discarded() || !body.is_object() || !body.contains("core") || !body.contains("game")) {
            error(res, 400, "Send { core, game } as JSON.");
            return;
        }
        auto core = inside(options.coresDir, body.value("core", ""));
        if (core.empty() || !std::filesystem::is_regular_file(core)) {
            error(res, 400, "The core must be a file in the cores folder.");
            return;
        }
        auto game = body.value("game", "");
        if (!std::filesystem::is_regular_file(game)) {
            error(res, 400, "The game file does not exist.");
            return;
        }

        SessionOptions opts;
        opts.paths.core = core;
        opts.paths.game = game;
        opts.paths.system = body.value("system", "");
        opts.paths.save = body.value("save", "");
        opts.encoder = body.value("encoder", options.encoder);
        opts.bitrateKbps = body.value("bitrateKbps", 8000);
        opts.portRangeBegin = options.portRangeBegin;
        opts.portRangeEnd = options.portRangeEnd;
        if (opts.encoder != "auto" && opts.encoder != "nvenc" && opts.encoder != "x264") {
            error(res, 400, "encoder is auto, nvenc or x264.");
            return;
        }

        std::lock_guard<std::mutex> lock(mutex);
        session.reset(); // the old game saves and stops first
        auto next = std::make_shared<Session>(randomId(), opts);
        try {
            auto sdp = next->start();
            session = next;
            res.set_content(json{{"id", next->id()}, {"sdp", sdp}}.dump(), "application/json");
        } catch (const std::exception &e) {
            error(res, 422, e.what());
        }
    });

    server.Post(R"(/sessions/([0-9a-f]{16})/answer)", [&](const httplib::Request &req, httplib::Response &res) {
        auto s = current(req.matches[1]);
        if (!s) {
            error(res, 404, "No such session.");
            return;
        }
        json body = json::parse(req.body, nullptr, false);
        if (body.is_discarded() || !body.contains("sdp") || !body["sdp"].is_string()) {
            error(res, 400, "Send { sdp } as JSON.");
            return;
        }
        try {
            s->answer(body["sdp"].get<std::string>());
            res.status = 204;
        } catch (const std::exception &e) {
            error(res, 400, e.what());
        }
    });

    server.Get(R"(/sessions/([0-9a-f]{16}))", [&](const httplib::Request &req, httplib::Response &res) {
        auto s = current(req.matches[1]);
        if (!s) {
            error(res, 404, "No such session.");
            return;
        }
        res.set_content(s->stats().dump(), "application/json");
    });

    server.Get(R"(/sessions/([0-9a-f]{16})/state)", [&](const httplib::Request &req, httplib::Response &res) {
        auto s = current(req.matches[1]);
        if (!s) {
            error(res, 404, "No such session.");
            return;
        }
        auto state = s->saveState();
        if (state.empty()) {
            error(res, 409, "The core could not save its state.");
            return;
        }
        res.set_content(std::string(state.begin(), state.end()), "application/octet-stream");
    });

    server.Put(R"(/sessions/([0-9a-f]{16})/state)", [&](const httplib::Request &req, httplib::Response &res) {
        auto s = current(req.matches[1]);
        if (!s) {
            error(res, 404, "No such session.");
            return;
        }
        if (!s->loadState(std::vector<uint8_t>(req.body.begin(), req.body.end()))) {
            error(res, 422, "The core did not accept that state.");
            return;
        }
        res.status = 204;
    });

    server.Delete(R"(/sessions/([0-9a-f]{16}))", [&](const httplib::Request &req, httplib::Response &res) {
        std::lock_guard<std::mutex> lock(mutex);
        if (session && session->id() == req.matches[1]) {
            session.reset();
        }
        res.status = 204;
    });

    if (!options.devPage.empty()) {
        server.Get("/dev", [&](const httplib::Request &, httplib::Response &res) {
            res.set_content(readText(options.devPage), "text/html; charset=utf-8");
        });
    }

    server.set_payload_max_length(64 * 1024 * 1024);

    int port = options.port > 0 ? (server.bind_to_port(options.bind, options.port) ? options.port : -1)
                                : server.bind_to_any_port(options.bind);
    if (port <= 0) {
        std::fprintf(stderr, "Could not listen on %s:%d\n", options.bind.c_str(), options.port);
        return 2;
    }

    g_server = &server;
    std::signal(SIGINT, onSignal);
    std::signal(SIGTERM, onSignal);

    // The supervisor reads this line to learn the port.
    std::printf("LISTENING %s:%d\n", options.bind.c_str(), port);
    std::fflush(stdout);

    server.listen_after_bind();
    g_server = nullptr;

    std::lock_guard<std::mutex> lock(mutex);
    session.reset();
    return 0;
}

} // namespace cplay
