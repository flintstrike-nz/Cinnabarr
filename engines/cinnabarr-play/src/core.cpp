#include "core.h"

#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <dlfcn.h>
#include <filesystem>
#include <fstream>
#include <stdexcept>

namespace cplay {

namespace {

// libretro calls back through plain C functions with no context pointer, so only one core can run per process.
// That matches the design (one GPU, one session at a time).
Core *g_active = nullptr;

void logPrintf(enum retro_log_level level, const char *fmt, ...) {
    if (level < RETRO_LOG_WARN) {
        return;
    }
    va_list args;
    va_start(args, fmt);
    std::fputs("[core] ", stderr);
    std::vfprintf(stderr, fmt, args);
    va_end(args);
}

std::vector<uint8_t> readFile(const std::string &path) {
    std::ifstream in(path, std::ios::binary);
    if (!in) {
        throw std::runtime_error("Cannot read " + path);
    }
    return std::vector<uint8_t>(std::istreambuf_iterator<char>(in), {});
}

template <typename T> void bind(void *handle, T &target, const char *symbol) {
    target = reinterpret_cast<T>(dlsym(handle, symbol));
    if (!target) {
        throw std::runtime_error(std::string("The core has no ") + symbol + "; is it a libretro core?");
    }
}

} // namespace

struct CoreCallbacks {
    static bool environment(unsigned cmd, void *data) { return g_active && g_active->environment(cmd, data); }
    static void video(const void *data, unsigned w, unsigned h, size_t pitch) {
        if (g_active) {
            g_active->video(data, w, h, pitch);
        }
    }
    static void audioSample(int16_t left, int16_t right) {
        if (g_active) {
            g_active->monoToStereo_[0] = left;
            g_active->monoToStereo_[1] = right;
            g_active->audio(g_active->monoToStereo_, 1);
        }
    }
    static size_t audioBatch(const int16_t *data, size_t frames) {
        if (g_active) {
            g_active->audio(data, frames);
        }
        return frames;
    }
    static void inputPoll() {}
    static int16_t inputState(unsigned port, unsigned device, unsigned index, unsigned id) {
        return g_active ? g_active->input(port, device, index, id) : 0;
    }
};

Core::Core(Paths paths) : paths_(std::move(paths)) {}

Core::~Core() {
    if (gameLoaded_) {
        writeSaveRam();
        retro_unload_game_();
    }
    if (handle_) {
        if (retro_deinit_) {
            retro_deinit_();
        }
        dlclose(handle_);
    }
    if (g_active == this) {
        g_active = nullptr;
    }
}

void Core::load(VideoSink video, AudioSink audio) {
    if (g_active) {
        throw std::runtime_error("Another core is already running in this process.");
    }
    videoSink_ = std::move(video);
    audioSink_ = std::move(audio);

    handle_ = dlopen(paths_.core.c_str(), RTLD_NOW | RTLD_LOCAL);
    if (!handle_) {
        throw std::runtime_error(std::string("Cannot load the core: ") + dlerror());
    }

    bind(handle_, retro_init_, "retro_init");
    bind(handle_, retro_deinit_, "retro_deinit");
    bind(handle_, retro_set_environment_, "retro_set_environment");
    bind(handle_, retro_set_video_refresh_, "retro_set_video_refresh");
    bind(handle_, retro_set_audio_sample_, "retro_set_audio_sample");
    bind(handle_, retro_set_audio_sample_batch_, "retro_set_audio_sample_batch");
    bind(handle_, retro_set_input_poll_, "retro_set_input_poll");
    bind(handle_, retro_set_input_state_, "retro_set_input_state");
    bind(handle_, retro_get_system_info_, "retro_get_system_info");
    bind(handle_, retro_get_system_av_info_, "retro_get_system_av_info");
    bind(handle_, retro_load_game_, "retro_load_game");
    bind(handle_, retro_unload_game_, "retro_unload_game");
    bind(handle_, retro_run_, "retro_run");
    bind(handle_, retro_serialize_size_, "retro_serialize_size");
    bind(handle_, retro_serialize_, "retro_serialize");
    bind(handle_, retro_unserialize_, "retro_unserialize");
    bind(handle_, retro_get_memory_data_, "retro_get_memory_data");
    bind(handle_, retro_get_memory_size_, "retro_get_memory_size");
    bind(handle_, retro_set_controller_port_device_, "retro_set_controller_port_device");

    g_active = this;
    retro_set_environment_(&CoreCallbacks::environment);
    retro_set_video_refresh_(&CoreCallbacks::video);
    retro_set_audio_sample_(&CoreCallbacks::audioSample);
    retro_set_audio_sample_batch_(&CoreCallbacks::audioBatch);
    retro_set_input_poll_(&CoreCallbacks::inputPoll);
    retro_set_input_state_(&CoreCallbacks::inputState);
    retro_init_();

    retro_get_system_info_(&systemInfo_);
    libraryName_ = systemInfo_.library_name ? systemInfo_.library_name : "core";

    retro_game_info game{};
    game.path = paths_.game.c_str();
    if (!systemInfo_.need_fullpath) {
        gameData_ = readFile(paths_.game);
        game.data = gameData_.data();
        game.size = gameData_.size();
    }
    if (!retro_load_game_(&game)) {
        throw std::runtime_error("The core could not load the game (wrong platform, a bad dump, or a missing BIOS).");
    }
    gameLoaded_ = true;

    for (unsigned port = 0; port < MaxPads; port++) {
        retro_set_controller_port_device_(port, RETRO_DEVICE_JOYPAD);
    }

    retro_get_system_av_info_(&avInfo_);
    readSaveRam();
}

void Core::runFrame() { retro_run_(); }

std::vector<uint8_t> Core::saveState() {
    size_t size = retro_serialize_size_();
    std::vector<uint8_t> state(size);
    if (size == 0 || !retro_serialize_(state.data(), size)) {
        return {};
    }
    return state;
}

bool Core::loadState(const std::vector<uint8_t> &state) {
    return !state.empty() && retro_unserialize_(state.data(), state.size());
}

std::string Core::saveRamPath() const {
    auto stem = std::filesystem::path(paths_.game).stem().string();
    return (std::filesystem::path(paths_.save) / (stem + ".srm")).string();
}

void Core::readSaveRam() {
    void *ram = retro_get_memory_data_(RETRO_MEMORY_SAVE_RAM);
    size_t size = retro_get_memory_size_(RETRO_MEMORY_SAVE_RAM);
    if (!ram || size == 0 || paths_.save.empty()) {
        return;
    }
    std::ifstream in(saveRamPath(), std::ios::binary);
    if (in) {
        in.read(static_cast<char *>(ram), static_cast<std::streamsize>(size));
    }
}

void Core::writeSaveRam() {
    void *ram = retro_get_memory_data_ ? retro_get_memory_data_(RETRO_MEMORY_SAVE_RAM) : nullptr;
    size_t size = retro_get_memory_size_ ? retro_get_memory_size_(RETRO_MEMORY_SAVE_RAM) : 0;
    if (!ram || size == 0 || paths_.save.empty()) {
        return;
    }
    std::error_code ignored;
    std::filesystem::create_directories(paths_.save, ignored);
    auto path = saveRamPath();
    auto temp = path + ".tmp";
    {
        std::ofstream out(temp, std::ios::binary | std::ios::trunc);
        out.write(static_cast<const char *>(ram), static_cast<std::streamsize>(size));
        if (!out) {
            return;
        }
    }
    std::filesystem::rename(temp, path, ignored);
}

bool Core::environment(unsigned cmd, void *data) {
    switch (cmd & ~RETRO_ENVIRONMENT_EXPERIMENTAL) {
    case RETRO_ENVIRONMENT_GET_CAN_DUPE:
        *static_cast<bool *>(data) = true;
        return true;
    case RETRO_ENVIRONMENT_SET_PIXEL_FORMAT: {
        auto format = *static_cast<const retro_pixel_format *>(data);
        if (format != RETRO_PIXEL_FORMAT_0RGB1555 && format != RETRO_PIXEL_FORMAT_XRGB8888 && format != RETRO_PIXEL_FORMAT_RGB565) {
            return false;
        }
        format_ = format;
        return true;
    }
    case RETRO_ENVIRONMENT_GET_SYSTEM_DIRECTORY:
        *static_cast<const char **>(data) = paths_.system.c_str();
        return true;
    case RETRO_ENVIRONMENT_GET_SAVE_DIRECTORY:
        *static_cast<const char **>(data) = paths_.save.c_str();
        return true;
    case RETRO_ENVIRONMENT_GET_LOG_INTERFACE:
        static_cast<retro_log_callback *>(data)->log = &logPrintf;
        return true;
    case RETRO_ENVIRONMENT_GET_INPUT_BITMASKS:
        return true;
    case RETRO_ENVIRONMENT_SET_GEOMETRY:
        avInfo_.geometry = *static_cast<const retro_game_geometry *>(data);
        return true;
    case RETRO_ENVIRONMENT_SET_SYSTEM_AV_INFO:
        avInfo_ = *static_cast<const retro_system_av_info *>(data);
        return true;
    case RETRO_ENVIRONMENT_GET_VARIABLE_UPDATE:
        *static_cast<bool *>(data) = false;
        return true;
    case RETRO_ENVIRONMENT_SET_INPUT_DESCRIPTORS:
    case RETRO_ENVIRONMENT_SET_CONTROLLER_INFO:
    case RETRO_ENVIRONMENT_SET_SUPPORT_NO_GAME:
    case RETRO_ENVIRONMENT_SET_PERFORMANCE_LEVEL:
    case RETRO_ENVIRONMENT_SET_MEMORY_MAPS:
    case RETRO_ENVIRONMENT_SET_SUBSYSTEM_INFO:
        return true;
    case RETRO_ENVIRONMENT_SET_HW_RENDER:
        // GPU-rendered cores (N64, PlayStation 2, GameCube, Wii) come with step G7c.
        return false;
    default:
        // Variables and core options: the core's defaults are used.
        return false;
    }
}

void Core::video(const void *data, unsigned width, unsigned height, size_t pitch) {
    if (!videoSink_ || data == RETRO_HW_FRAME_BUFFER_VALID) {
        return;
    }
    VideoFrame frame{data, width, height, pitch, format_};
    videoSink_(frame);
}

void Core::audio(const int16_t *data, size_t frames) {
    if (audioSink_ && data && frames) {
        audioSink_(data, frames);
    }
}

int16_t Core::input(unsigned port, unsigned device, unsigned index, unsigned id) {
    if (port >= MaxPads) {
        return 0;
    }
    auto &pad = pads[port];
    switch (device & RETRO_DEVICE_MASK) {
    case RETRO_DEVICE_JOYPAD:
        if (id == RETRO_DEVICE_ID_JOYPAD_MASK) {
            return static_cast<int16_t>(pad.buttons.load());
        }
        return id < 16 ? static_cast<int16_t>((pad.buttons.load() >> id) & 1) : 0;
    case RETRO_DEVICE_ANALOG:
        if (index <= RETRO_DEVICE_INDEX_ANALOG_RIGHT && id <= RETRO_DEVICE_ID_ANALOG_Y) {
            return pad.axes[index * 2 + id].load();
        }
        return 0;
    default:
        return 0;
    }
}

} // namespace cplay
