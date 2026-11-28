// FFmpeg encoders for the stream: H.264 video tuned for latency (NVIDIA NVENC when there is one, else x264) and
// Opus audio at 48 kHz.
#pragma once

#include <cstdint>
#include <functional>
#include <string>
#include <vector>

#include "core.h"

struct AVCodecContext;
struct AVFrame;
struct AVPacket;
struct SwsContext;
struct SwrContext;

namespace cplay {

struct EncodedVideo {
    std::vector<uint8_t> data; // Annex B
    int64_t index = 0;         // frame number since the start
    bool keyframe = false;
};

class VideoEncoder {
public:
    // encoder: "auto" (NVENC if it works, else x264), "nvenc" or "x264".
    VideoEncoder(std::string encoder, int bitrateKbps, double fps);
    ~VideoEncoder();
    VideoEncoder(const VideoEncoder &) = delete;
    VideoEncoder &operator=(const VideoEncoder &) = delete;

    // Encodes one frame; the size may change between frames (the encoder is then rebuilt).
    bool encode(const VideoFrame &frame, std::vector<EncodedVideo> &out);

    // The next frame is a keyframe (a viewer joined or lost packets).
    void requestKeyframe() { keyframe_ = true; }

    const std::string &name() const { return name_; }
    unsigned width() const { return width_; }
    unsigned height() const { return height_; }

private:
    void open(unsigned width, unsigned height);
    void close();
    bool tryOpen(const char *codec, unsigned width, unsigned height);

    std::string requested_;
    std::string name_;
    int bitrateKbps_;
    double fps_;
    unsigned width_ = 0;
    unsigned height_ = 0;
    int sourceFormat_ = -1;
    unsigned sourceWidth_ = 0;
    unsigned sourceHeight_ = 0;
    int64_t index_ = 0;
    bool keyframe_ = true;
    AVCodecContext *context_ = nullptr;
    AVFrame *frame_ = nullptr;
    AVPacket *packet_ = nullptr;
    SwsContext *scaler_ = nullptr;
    std::vector<uint8_t> last_; // the last picture, for frames the core repeats
    size_t lastPitch_ = 0;
};

class AudioEncoder {
public:
    explicit AudioEncoder(double inputRate);
    ~AudioEncoder();
    AudioEncoder(const AudioEncoder &) = delete;
    AudioEncoder &operator=(const AudioEncoder &) = delete;

    // Takes the core's stereo samples; produces 20 ms Opus packets. timestamp is in 48 kHz samples.
    void push(const int16_t *stereo, size_t frames, std::function<void(const std::vector<uint8_t> &, int64_t timestamp)> sink);

    static constexpr int Rate = 48000;
    static constexpr int FrameSamples = 960; // 20 ms

private:
    AVCodecContext *context_ = nullptr;
    AVFrame *frame_ = nullptr;
    AVPacket *packet_ = nullptr;
    SwrContext *resampler_ = nullptr;
    std::vector<int16_t> pending_; // resampled, interleaved stereo, not yet a whole Opus frame
    int64_t samples_ = 0;
};

} // namespace cplay
