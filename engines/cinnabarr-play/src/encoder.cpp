#include "encoder.h"

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <stdexcept>

extern "C" {
#include <libavcodec/avcodec.h>
#include <libavutil/channel_layout.h>
#include <libavutil/imgutils.h>
#include <libavutil/opt.h>
#include <libswresample/swresample.h>
#include <libswscale/swscale.h>
}

namespace cplay {

namespace {

AVPixelFormat toAv(retro_pixel_format format) {
    switch (format) {
    case RETRO_PIXEL_FORMAT_XRGB8888:
        return AV_PIX_FMT_BGR0; // 0x00RRGGBB in little-endian memory is B, G, R, 0
    case RETRO_PIXEL_FORMAT_RGB565:
        return AV_PIX_FMT_RGB565LE;
    default:
        return AV_PIX_FMT_RGB555LE;
    }
}

unsigned bytesPerPixel(retro_pixel_format format) { return format == RETRO_PIXEL_FORMAT_XRGB8888 ? 4 : 2; }

} // namespace

VideoEncoder::VideoEncoder(std::string encoder, int bitrateKbps, double fps)
    : requested_(std::move(encoder)), bitrateKbps_(bitrateKbps), fps_(fps) {}

VideoEncoder::~VideoEncoder() { close(); }

void VideoEncoder::close() {
    if (context_) {
        avcodec_free_context(&context_);
    }
    if (frame_) {
        av_frame_free(&frame_);
    }
    if (packet_) {
        av_packet_free(&packet_);
    }
    if (scaler_) {
        sws_freeContext(scaler_);
        scaler_ = nullptr;
    }
}

bool VideoEncoder::tryOpen(const char *codecName, unsigned width, unsigned height) {
    const AVCodec *codec = avcodec_find_encoder_by_name(codecName);
    if (!codec) {
        return false;
    }
    AVCodecContext *ctx = avcodec_alloc_context3(codec);
    int fpsMilli = static_cast<int>(std::lround(fps_ * 1000));
    ctx->width = static_cast<int>(width);
    ctx->height = static_cast<int>(height);
    ctx->time_base = AVRational{1000, fpsMilli};
    ctx->framerate = AVRational{fpsMilli, 1000};
    ctx->pix_fmt = AV_PIX_FMT_YUV420P;
    ctx->gop_size = static_cast<int>(fps_ * 10); // keyframes also come on request (a viewer joins or loses packets)
    ctx->max_b_frames = 0;
    ctx->flags |= AV_CODEC_FLAG_LOW_DELAY;

    // Enough for sharp retro pictures, capped by the setting.
    double wanted = width * height * fps_ * 0.3 / 1000.0;
    int kbps = static_cast<int>(std::clamp(wanted, 1500.0, static_cast<double>(bitrateKbps_)));
    ctx->bit_rate = static_cast<int64_t>(kbps) * 1000;
    ctx->rc_max_rate = ctx->bit_rate;
    ctx->rc_buffer_size = static_cast<int>(ctx->bit_rate / fps_ * 2); // about two frames: no queue builds up

    if (std::strcmp(codecName, "libx264") == 0) {
        av_opt_set(ctx->priv_data, "preset", "ultrafast", 0);
        av_opt_set(ctx->priv_data, "tune", "zerolatency", 0);
        av_opt_set(ctx->priv_data, "profile", "baseline", 0);
        av_opt_set(ctx->priv_data, "x264-params", "repeat-headers=1", 0);
    } else {
        av_opt_set(ctx->priv_data, "preset", "p1", 0);
        av_opt_set(ctx->priv_data, "tune", "ull", 0);
        av_opt_set(ctx->priv_data, "zerolatency", "1", 0);
        av_opt_set(ctx->priv_data, "rc", "cbr", 0);
        av_opt_set(ctx->priv_data, "profile", "baseline", 0);
        av_opt_set(ctx->priv_data, "delay", "0", 0);
        av_opt_set(ctx->priv_data, "forced-idr", "1", 0);
    }

    if (avcodec_open2(ctx, codec, nullptr) < 0) {
        avcodec_free_context(&ctx);
        return false;
    }
    context_ = ctx;
    name_ = codecName;
    return true;
}

void VideoEncoder::open(unsigned width, unsigned height) {
    close();
    bool ok = false;
    if (requested_ == "nvenc" || requested_ == "auto") {
        ok = tryOpen("h264_nvenc", width, height);
    }
    if (!ok && (requested_ == "x264" || requested_ == "auto")) {
        ok = tryOpen("libx264", width, height);
    }
    if (!ok) {
        throw std::runtime_error("No H.264 encoder could be opened (" + requested_ + ").");
    }
    frame_ = av_frame_alloc();
    frame_->format = AV_PIX_FMT_YUV420P;
    frame_->width = static_cast<int>(width);
    frame_->height = static_cast<int>(height);
    av_frame_get_buffer(frame_, 0);
    packet_ = av_packet_alloc();
    width_ = width;
    height_ = height;
    keyframe_ = true;
    std::fprintf(stderr, "video: %s %ux%u at %.2f fps\n", name_.c_str(), width, height, fps_);
}

bool VideoEncoder::encode(const VideoFrame &in, std::vector<EncodedVideo> &out) {
    const uint8_t *pixels = static_cast<const uint8_t *>(in.data);
    unsigned w = in.width;
    unsigned h = in.height;
    size_t pitch = in.pitch;
    retro_pixel_format format = in.format;

    if (pixels) {
        // Keep a copy: the core may repeat this frame by passing null.
        last_.assign(pixels, pixels + pitch * h);
        lastPitch_ = pitch;
        sourceWidth_ = w;
        sourceHeight_ = h;
        sourceFormat_ = format;
    } else {
        if (last_.empty()) {
            return false;
        }
        pixels = last_.data();
        pitch = lastPitch_;
        w = sourceWidth_;
        h = sourceHeight_;
        format = static_cast<retro_pixel_format>(sourceFormat_);
    }
    if (w == 0 || h == 0 || pitch < w * bytesPerPixel(format)) {
        return false;
    }

    // H.264 wants even sizes.
    unsigned ew = (w + 1) & ~1u;
    unsigned eh = (h + 1) & ~1u;
    if (!context_ || ew != width_ || eh != height_) {
        open(ew, eh);
    }
    scaler_ = sws_getCachedContext(scaler_, static_cast<int>(w), static_cast<int>(h), toAv(format), static_cast<int>(ew),
                                   static_cast<int>(eh), AV_PIX_FMT_YUV420P, SWS_POINT, nullptr, nullptr, nullptr);
    if (!scaler_) {
        return false;
    }

    av_frame_make_writable(frame_);
    const uint8_t *src[1] = {pixels};
    int srcStride[1] = {static_cast<int>(pitch)};
    sws_scale(scaler_, src, srcStride, 0, static_cast<int>(h), frame_->data, frame_->linesize);

    frame_->pts = index_++;
    if (keyframe_) {
        frame_->pict_type = AV_PICTURE_TYPE_I;
        frame_->flags |= AV_FRAME_FLAG_KEY;
        keyframe_ = false;
    } else {
        frame_->pict_type = AV_PICTURE_TYPE_NONE;
        frame_->flags &= ~AV_FRAME_FLAG_KEY;
    }

    if (avcodec_send_frame(context_, frame_) < 0) {
        return false;
    }
    while (avcodec_receive_packet(context_, packet_) == 0) {
        EncodedVideo encoded;
        encoded.data.assign(packet_->data, packet_->data + packet_->size);
        encoded.index = packet_->pts;
        encoded.keyframe = (packet_->flags & AV_PKT_FLAG_KEY) != 0;
        out.push_back(std::move(encoded));
        av_packet_unref(packet_);
    }
    return true;
}

AudioEncoder::AudioEncoder(double inputRate) {
    const AVCodec *codec = avcodec_find_encoder_by_name("libopus");
    if (!codec) {
        throw std::runtime_error("FFmpeg has no libopus encoder.");
    }
    context_ = avcodec_alloc_context3(codec);
    context_->sample_rate = Rate;
    av_channel_layout_default(&context_->ch_layout, 2);
    context_->sample_fmt = AV_SAMPLE_FMT_S16;
    context_->bit_rate = 96000;
    context_->time_base = AVRational{1, Rate};
    av_opt_set(context_->priv_data, "application", "lowdelay", 0);
    av_opt_set(context_->priv_data, "frame_duration", "20", 0);
    if (avcodec_open2(context_, codec, nullptr) < 0) {
        avcodec_free_context(&context_);
        throw std::runtime_error("Could not open the Opus encoder.");
    }

    frame_ = av_frame_alloc();
    frame_->format = AV_SAMPLE_FMT_S16;
    frame_->nb_samples = context_->frame_size > 0 ? context_->frame_size : FrameSamples;
    av_channel_layout_copy(&frame_->ch_layout, &context_->ch_layout);
    frame_->sample_rate = Rate;
    av_frame_get_buffer(frame_, 0);
    packet_ = av_packet_alloc();

    AVChannelLayout stereo;
    av_channel_layout_default(&stereo, 2);
    if (swr_alloc_set_opts2(&resampler_, &stereo, AV_SAMPLE_FMT_S16, Rate, &stereo, AV_SAMPLE_FMT_S16,
                            static_cast<int>(std::lround(inputRate)), 0, nullptr) < 0 ||
        swr_init(resampler_) < 0) {
        throw std::runtime_error("Could not set up audio resampling.");
    }
    av_channel_layout_uninit(&stereo);
}

AudioEncoder::~AudioEncoder() {
    if (resampler_) {
        swr_free(&resampler_);
    }
    if (packet_) {
        av_packet_free(&packet_);
    }
    if (frame_) {
        av_frame_free(&frame_);
    }
    if (context_) {
        avcodec_free_context(&context_);
    }
}

void AudioEncoder::push(const int16_t *stereo, size_t frames,
                        std::function<void(const std::vector<uint8_t> &, int64_t timestamp)> sink) {
    int capacity = swr_get_out_samples(resampler_, static_cast<int>(frames));
    if (capacity <= 0) {
        return;
    }
    size_t before = pending_.size();
    pending_.resize(before + static_cast<size_t>(capacity) * 2);
    uint8_t *outPtr = reinterpret_cast<uint8_t *>(pending_.data() + before);
    const uint8_t *inPtr = reinterpret_cast<const uint8_t *>(stereo);
    int converted = swr_convert(resampler_, &outPtr, capacity, &inPtr, static_cast<int>(frames));
    pending_.resize(before + static_cast<size_t>(std::max(converted, 0)) * 2);

    size_t per = static_cast<size_t>(frame_->nb_samples) * 2;
    while (pending_.size() >= per) {
        av_frame_make_writable(frame_);
        std::memcpy(frame_->data[0], pending_.data(), per * sizeof(int16_t));
        pending_.erase(pending_.begin(), pending_.begin() + static_cast<std::ptrdiff_t>(per));
        frame_->pts = samples_;
        samples_ += frame_->nb_samples;
        if (avcodec_send_frame(context_, frame_) < 0) {
            continue;
        }
        while (avcodec_receive_packet(context_, packet_) == 0) {
            std::vector<uint8_t> data(packet_->data, packet_->data + packet_->size);
            sink(data, packet_->pts);
            av_packet_unref(packet_);
        }
    }
}

} // namespace cplay
