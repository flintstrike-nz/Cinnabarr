// One game being played: the core, the encoders and the WebRTC connection to one viewer.
#pragma once

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <functional>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

#include <nlohmann/json.hpp>

#include "core.h"

namespace rtc {
class PeerConnection;
class Track;
class DataChannel;
} // namespace rtc

namespace cplay {

class VideoEncoder;
class AudioEncoder;

struct SessionOptions {
    Core::Paths paths;
    std::string encoder = "auto"; // auto, nvenc, x264
    int bitrateKbps = 8000;
    uint16_t portRangeBegin = 0; // 0: any port
    uint16_t portRangeEnd = 0;
    std::vector<uint8_t> state; // a save state to start from, or empty
};

// The input message the browser sends on the "input" data channel, little-endian:
//   u8 version (1), u8 port, u16 buttons (libretro joypad bits), i16 left x, left y, right x, right y
constexpr size_t InputMessageSize = 12;

class Session {
public:
    Session(std::string id, SessionOptions options);
    ~Session();
    Session(const Session &) = delete;
    Session &operator=(const Session &) = delete;

    // Loads the game and returns the WebRTC offer (all ICE candidates included). Throws with a reason.
    std::string start();

    // The browser's answer to the offer.
    void answer(const std::string &sdp);

    const std::string &id() const { return id_; }
    bool ended() const { return ended_.load(); }
    nlohmann::json stats();

    // Run on the game thread; empty / false when the core can't.
    std::vector<uint8_t> saveState();
    bool loadState(std::vector<uint8_t> state);

    // Applies one input message; false when it is malformed.
    bool applyInput(const uint8_t *data, size_t size);

private:
    void loop();
    std::vector<uint8_t> runOnGameThread(std::function<std::vector<uint8_t>()> work);

    std::string id_;
    SessionOptions options_;
    std::unique_ptr<Core> core_;
    std::unique_ptr<VideoEncoder> video_;
    std::unique_ptr<AudioEncoder> audio_;
    std::shared_ptr<rtc::PeerConnection> pc_;
    std::shared_ptr<rtc::Track> videoTrack_;
    std::shared_ptr<rtc::Track> audioTrack_;
    std::shared_ptr<rtc::DataChannel> input_;

    std::thread thread_;
    std::atomic<bool> running_{false};
    std::atomic<bool> connected_{false};
    std::atomic<bool> ended_{false};
    std::atomic<bool> keyframeWanted_{true};

    std::mutex tasksMutex_;
    std::vector<std::function<void()>> tasks_;

    // Stats.
    std::chrono::steady_clock::time_point started_ = std::chrono::steady_clock::now();
    std::atomic<int64_t> framesSent_{0};
    std::atomic<int64_t> bytesSent_{0};
    std::atomic<int64_t> inputMessages_{0};
    std::atomic<int64_t> lateFrames_{0};
    std::atomic<double> lastFrameMs_{0};
};

} // namespace cplay
