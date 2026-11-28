// The control API Cinnabarr uses (localhost only, with an API key): start a game, hand over the WebRTC offer and
// answer, read stats, save and load states, stop.
#pragma once

#include <cstdint>
#include <string>

namespace cplay {

struct ApiOptions {
    std::string bind = "127.0.0.1";
    int port = 0; // 0: any free port
    std::string apiKey;
    std::string coresDir;  // cores may only be loaded from here
    std::string devPage;   // a test page to serve at /dev, or empty
    uint16_t portRangeBegin = 0;
    uint16_t portRangeEnd = 0;
    std::string encoder = "auto";
};

// Runs until the process is asked to stop. Returns the exit code.
int runApi(const ApiOptions &options);

} // namespace cplay
