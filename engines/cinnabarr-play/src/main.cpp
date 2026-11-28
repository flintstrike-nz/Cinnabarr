// cinnabarr-play: runs a libretro core on the server and streams it to a browser over WebRTC.
//
//   cinnabarr-play --cores <dir> --api-key-file <file> [--port N] [--bind 127.0.0.1]
//                  [--encoder auto|nvenc|x264] [--udp-ports 50000-50100] [--dev-page test/dev.html]
//
// Cinnabarr starts it (step G7b) the way it starts its torrent engine: on a free localhost port, with a key it
// generated. --dev-page and a --bind other than 127.0.0.1 are for trying it out by hand (step G7a).

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <random>
#include <string>

#include "api.h"

namespace {

void usage() {
    std::fprintf(stderr,
                 "cinnabarr-play %s\n"
                 "  --cores <dir>          folder the libretro cores are in (required)\n"
                 "  --api-key-file <file>  file with the API key; created with a new key if missing (required)\n"
                 "  --port <n>             port for the control API (default: any free port)\n"
                 "  --bind <address>       address for the control API (default 127.0.0.1)\n"
                 "  --encoder <name>       auto (NVENC if it works, else x264), nvenc or x264\n"
                 "  --udp-ports <a-b>      UDP ports WebRTC may use (for Docker port mapping)\n"
                 "  --dev-page <file>      serve a test page at /dev (for trying it by hand)\n",
                 CINNABARR_PLAY_VERSION);
}

std::string readKey(const std::string &path) {
    std::ifstream in(path);
    std::string key;
    std::getline(in, key);
    while (!key.empty() && (key.back() == '\r' || key.back() == '\n' || key.back() == ' ')) {
        key.pop_back();
    }
    if (!key.empty()) {
        return key;
    }
    std::random_device device;
    std::uniform_int_distribution<int> digit(0, 15);
    for (int i = 0; i < 32; i++) {
        key += "0123456789abcdef"[digit(device)];
    }
    std::ofstream out(path, std::ios::trunc);
    out << key << "\n";
    std::filesystem::permissions(path, std::filesystem::perms::owner_read | std::filesystem::perms::owner_write);
    return key;
}

} // namespace

int main(int argc, char **argv) {
    cplay::ApiOptions options;
    std::string keyFile;

    for (int i = 1; i < argc; i++) {
        std::string arg = argv[i];
        auto next = [&]() -> std::string {
            if (i + 1 >= argc) {
                usage();
                std::exit(1);
            }
            return argv[++i];
        };
        if (arg == "--cores") {
            options.coresDir = next();
        } else if (arg == "--api-key-file") {
            keyFile = next();
        } else if (arg == "--port") {
            options.port = std::atoi(next().c_str());
        } else if (arg == "--bind") {
            options.bind = next();
        } else if (arg == "--encoder") {
            options.encoder = next();
        } else if (arg == "--udp-ports") {
            auto range = next();
            auto dash = range.find('-');
            if (dash == std::string::npos) {
                usage();
                return 1;
            }
            options.portRangeBegin = static_cast<uint16_t>(std::atoi(range.substr(0, dash).c_str()));
            options.portRangeEnd = static_cast<uint16_t>(std::atoi(range.substr(dash + 1).c_str()));
        } else if (arg == "--dev-page") {
            options.devPage = next();
        } else if (arg == "--version") {
            std::printf("%s\n", CINNABARR_PLAY_VERSION);
            return 0;
        } else {
            usage();
            return arg == "--help" ? 0 : 1;
        }
    }

    if (options.coresDir.empty() || keyFile.empty()) {
        usage();
        return 1;
    }
    if (options.encoder != "auto" && options.encoder != "nvenc" && options.encoder != "x264") {
        usage();
        return 1;
    }

    options.apiKey = readKey(keyFile);
    return cplay::runApi(options);
}
