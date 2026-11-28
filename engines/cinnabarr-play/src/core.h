// A libretro core loaded from a shared library: the same cores RetroArch uses, without RetroArch.
#pragma once

#include <atomic>
#include <cstdint>
#include <functional>
#include <mutex>
#include <string>
#include <vector>

#include "libretro.h"

namespace cplay {

// What the player is pressing, as libretro joypad bits (RETRO_DEVICE_ID_JOYPAD_*) and analog sticks.
struct PadState {
    std::atomic<uint16_t> buttons{0};
    std::atomic<int16_t> axes[4]{}; // left x, left y, right x, right y
};

constexpr int MaxPads = 4;

struct VideoFrame {
    const void *data = nullptr; // null when the core repeats the last frame
    unsigned width = 0;
    unsigned height = 0;
    size_t pitch = 0;
    retro_pixel_format format = RETRO_PIXEL_FORMAT_0RGB1555;
};

class Core {
public:
    struct Paths {
        std::string core;   // the core library
        std::string game;   // the ROM or disc image
        std::string system; // where BIOS files are (RETRO_ENVIRONMENT_GET_SYSTEM_DIRECTORY)
        std::string save;   // where the core keeps its own files
    };

    using VideoSink = std::function<void(const VideoFrame &)>;
    using AudioSink = std::function<void(const int16_t *stereo, size_t frames)>;

    explicit Core(Paths paths);
    ~Core();
    Core(const Core &) = delete;
    Core &operator=(const Core &) = delete;

    // Loads the core and the game; throws std::runtime_error with a reason when either fails.
    void load(VideoSink video, AudioSink audio);

    // Runs one frame. Not thread safe: call from one thread.
    void runFrame();

    double fps() const { return avInfo_.timing.fps > 0 ? avInfo_.timing.fps : 60.0; }
    double sampleRate() const { return avInfo_.timing.sample_rate > 0 ? avInfo_.timing.sample_rate : 48000.0; }
    const std::string &name() const { return libraryName_; }

    PadState pads[MaxPads];

    // The core's whole state (retro_serialize), or empty when it can't.
    std::vector<uint8_t> saveState();
    bool loadState(const std::vector<uint8_t> &state);

    // Battery saves (cartridge SRAM), written next to the game in the save folder.
    void writeSaveRam();

private:
    friend struct CoreCallbacks;

    bool environment(unsigned cmd, void *data);
    void video(const void *data, unsigned width, unsigned height, size_t pitch);
    void audio(const int16_t *data, size_t frames);
    int16_t input(unsigned port, unsigned device, unsigned index, unsigned id);
    std::string saveRamPath() const;
    void readSaveRam();

    Paths paths_;
    void *handle_ = nullptr;
    bool gameLoaded_ = false;
    retro_pixel_format format_ = RETRO_PIXEL_FORMAT_0RGB1555;
    retro_system_info systemInfo_{};
    retro_system_av_info avInfo_{};
    std::string libraryName_;
    std::vector<uint8_t> gameData_;
    VideoSink videoSink_;
    AudioSink audioSink_;
    int16_t monoToStereo_[2]{};

    // The core's functions.
    void (*retro_init_)() = nullptr;
    void (*retro_deinit_)() = nullptr;
    void (*retro_set_environment_)(retro_environment_t) = nullptr;
    void (*retro_set_video_refresh_)(retro_video_refresh_t) = nullptr;
    void (*retro_set_audio_sample_)(retro_audio_sample_t) = nullptr;
    void (*retro_set_audio_sample_batch_)(retro_audio_sample_batch_t) = nullptr;
    void (*retro_set_input_poll_)(retro_input_poll_t) = nullptr;
    void (*retro_set_input_state_)(retro_input_state_t) = nullptr;
    void (*retro_get_system_info_)(retro_system_info *) = nullptr;
    void (*retro_get_system_av_info_)(retro_system_av_info *) = nullptr;
    bool (*retro_load_game_)(const retro_game_info *) = nullptr;
    void (*retro_unload_game_)() = nullptr;
    void (*retro_run_)() = nullptr;
    size_t (*retro_serialize_size_)() = nullptr;
    bool (*retro_serialize_)(void *, size_t) = nullptr;
    bool (*retro_unserialize_)(const void *, size_t) = nullptr;
    void *(*retro_get_memory_data_)(unsigned) = nullptr;
    size_t (*retro_get_memory_size_)(unsigned) = nullptr;
    void (*retro_set_controller_port_device_)(unsigned, unsigned) = nullptr;
};

} // namespace cplay
