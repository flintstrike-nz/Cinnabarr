#include "session.h"

#include <cstdio>
#include <cstring>
#include <future>
#include <stdexcept>

#include <rtc/rtc.hpp>

#include "encoder.h"

namespace cplay {

namespace {

constexpr uint8_t VideoPayloadType = 102;
constexpr uint8_t AudioPayloadType = 111;
constexpr uint32_t VideoSsrc = 1;
constexpr uint32_t AudioSsrc = 2;

int16_t readI16(const uint8_t *p) { return static_cast<int16_t>(p[0] | (p[1] << 8)); }

} // namespace

Session::Session(std::string id, SessionOptions options) : id_(std::move(id)), options_(std::move(options)) {}

Session::~Session() {
    running_ = false;
    if (thread_.joinable()) {
        thread_.join();
    }
    if (pc_) {
        pc_->close();
    }
    // The core goes last: its destructor writes battery saves.
    video_.reset();
    audio_.reset();
    core_.reset();
}

std::string Session::start() {
    core_ = std::make_unique<Core>(options_.paths);
    core_->load(
        [this](const VideoFrame &frame) {
            if (!connected_ || !videoTrack_ || !videoTrack_->isOpen()) {
                return;
            }
            if (keyframeWanted_.exchange(false)) {
                video_->requestKeyframe();
            }
            std::vector<EncodedVideo> packets;
            auto began = std::chrono::steady_clock::now();
            if (!video_->encode(frame, packets)) {
                return;
            }
            lastFrameMs_ = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - began).count();
            for (auto &packet : packets) {
                auto at = std::chrono::duration<double>(static_cast<double>(packet.index) / core_->fps());
                videoTrack_->sendFrame(reinterpret_cast<const rtc::byte *>(packet.data.data()), packet.data.size(), rtc::FrameInfo(at));
                framesSent_++;
                bytesSent_ += static_cast<int64_t>(packet.data.size());
            }
        },
        [this](const int16_t *stereo, size_t frames) {
            if (!connected_ || !audioTrack_ || !audioTrack_->isOpen()) {
                return;
            }
            audio_->push(stereo, frames, [this](const std::vector<uint8_t> &data, int64_t pts) {
                auto at = std::chrono::duration<double>(static_cast<double>(pts) / AudioEncoder::Rate);
                audioTrack_->sendFrame(reinterpret_cast<const rtc::byte *>(data.data()), data.size(), rtc::FrameInfo(at));
            });
        });

    if (!options_.state.empty() && !core_->loadState(options_.state)) {
        std::fprintf(stderr, "session %s: the save state did not load; starting fresh\n", id_.c_str());
    }

    video_ = std::make_unique<VideoEncoder>(options_.encoder, options_.bitrateKbps, core_->fps());
    audio_ = std::make_unique<AudioEncoder>(core_->sampleRate());

    rtc::Configuration config;
    config.disableAutoNegotiation = true;
    if (options_.portRangeBegin && options_.portRangeEnd >= options_.portRangeBegin) {
        config.portRangeBegin = options_.portRangeBegin;
        config.portRangeEnd = options_.portRangeEnd;
    }
    pc_ = std::make_shared<rtc::PeerConnection>(config);

    auto video = rtc::Description::Video("video", rtc::Description::Direction::SendOnly);
    video.addH264Codec(VideoPayloadType);
    video.addSSRC(VideoSsrc, "cinnabarr-video", "cinnabarr", "cinnabarr-video");
    videoTrack_ = pc_->addTrack(video);
    auto videoRtp = std::make_shared<rtc::RtpPacketizationConfig>(VideoSsrc, "cinnabarr-video", VideoPayloadType,
                                                                  rtc::H264RtpPacketizer::ClockRate);
    auto videoPacketizer = std::make_shared<rtc::H264RtpPacketizer>(rtc::NalUnit::Separator::StartSequence, videoRtp);
    videoPacketizer->addToChain(std::make_shared<rtc::RtcpSrReporter>(videoRtp));
    videoPacketizer->addToChain(std::make_shared<rtc::RtcpNackResponder>());
    videoPacketizer->addToChain(std::make_shared<rtc::PliHandler>([this]() { keyframeWanted_ = true; }));
    videoTrack_->setMediaHandler(videoPacketizer);

    auto audio = rtc::Description::Audio("audio", rtc::Description::Direction::SendOnly);
    audio.addOpusCodec(AudioPayloadType);
    audio.addSSRC(AudioSsrc, "cinnabarr-audio", "cinnabarr", "cinnabarr-audio");
    audioTrack_ = pc_->addTrack(audio);
    auto audioRtp = std::make_shared<rtc::RtpPacketizationConfig>(AudioSsrc, "cinnabarr-audio", AudioPayloadType,
                                                                  rtc::OpusRtpPacketizer::DefaultClockRate);
    auto audioPacketizer = std::make_shared<rtc::OpusRtpPacketizer>(audioRtp);
    audioPacketizer->addToChain(std::make_shared<rtc::RtcpSrReporter>(audioRtp));
    audioTrack_->setMediaHandler(audioPacketizer);

    // Input: unordered, no retransmits; a late button state is worse than a lost one, and the next one follows.
    rtc::DataChannelInit init;
    init.reliability.unordered = true;
    init.reliability.maxRetransmits = 0;
    input_ = pc_->createDataChannel("input", init);
    input_->onMessage([this](rtc::binary message) { applyInput(reinterpret_cast<const uint8_t *>(message.data()), message.size()); },
                      [](rtc::string) {});

    videoTrack_->onOpen([this]() {
        connected_ = true;
        keyframeWanted_ = true;
    });
    pc_->onStateChange([this](rtc::PeerConnection::State state) {
        if (state == rtc::PeerConnection::State::Disconnected || state == rtc::PeerConnection::State::Failed ||
            state == rtc::PeerConnection::State::Closed) {
            connected_ = false;
            ended_ = true;
        }
    });

    std::promise<void> gathered;
    auto done = gathered.get_future();
    std::atomic<bool> signalled{false};
    pc_->onGatheringStateChange([&gathered, &signalled](rtc::PeerConnection::GatheringState state) {
        if (state == rtc::PeerConnection::GatheringState::Complete && !signalled.exchange(true)) {
            gathered.set_value();
        }
    });
    pc_->setLocalDescription(rtc::Description::Type::Offer);
    if (done.wait_for(std::chrono::seconds(5)) != std::future_status::ready) {
        std::fprintf(stderr, "session %s: ICE gathering is slow; sending the candidates found so far\n", id_.c_str());
    }
    pc_->onGatheringStateChange(nullptr);

    auto description = pc_->localDescription();
    if (!description) {
        throw std::runtime_error("Could not create the WebRTC offer.");
    }

    running_ = true;
    thread_ = std::thread([this]() { loop(); });
    return std::string(*description);
}

void Session::answer(const std::string &sdp) {
    pc_->setRemoteDescription(rtc::Description(sdp, rtc::Description::Type::Answer));
}

bool Session::applyInput(const uint8_t *data, size_t size) {
    if (size < InputMessageSize || data[0] != 1 || data[1] >= MaxPads || !core_) {
        return false;
    }
    auto &pad = core_->pads[data[1]];
    pad.buttons = static_cast<uint16_t>(data[2] | (data[3] << 8));
    for (int axis = 0; axis < 4; axis++) {
        pad.axes[axis] = readI16(data + 4 + axis * 2);
    }
    inputMessages_++;
    return true;
}

void Session::loop() {
    using clock = std::chrono::steady_clock;
    auto frameTime = std::chrono::duration_cast<clock::duration>(std::chrono::duration<double>(1.0 / core_->fps()));
    auto next = clock::now();

    while (running_) {
        {
            std::vector<std::function<void()>> tasks;
            {
                std::lock_guard<std::mutex> lock(tasksMutex_);
                tasks.swap(tasks_);
            }
            for (auto &task : tasks) {
                task();
            }
        }

        if (!connected_) {
            // Nobody is watching yet (or any more): the game waits.
            std::this_thread::sleep_for(std::chrono::milliseconds(5));
            next = clock::now();
            continue;
        }

        core_->runFrame();

        next += frameTime;
        auto now = clock::now();
        if (now > next + frameTime * 5) {
            // Far behind (the machine is too slow, or it was paused): catch up instead of racing.
            lateFrames_++;
            next = now;
        } else if (now < next) {
            std::this_thread::sleep_until(next);
        }
    }

    // Leftover requests get an answer rather than waiting forever.
    std::lock_guard<std::mutex> lock(tasksMutex_);
    for (auto &task : tasks_) {
        task();
    }
    tasks_.clear();
}

std::vector<uint8_t> Session::runOnGameThread(std::function<std::vector<uint8_t>()> work) {
    if (!running_) {
        return work();
    }
    auto promise = std::make_shared<std::promise<std::vector<uint8_t>>>();
    auto future = promise->get_future();
    {
        std::lock_guard<std::mutex> lock(tasksMutex_);
        tasks_.push_back([promise, work]() { promise->set_value(work()); });
    }
    if (future.wait_for(std::chrono::seconds(10)) != std::future_status::ready) {
        return {};
    }
    return future.get();
}

std::vector<uint8_t> Session::saveState() {
    return runOnGameThread([this]() { return core_->saveState(); });
}

bool Session::loadState(std::vector<uint8_t> state) {
    auto result = runOnGameThread([this, state = std::move(state)]() {
        return core_->loadState(state) ? std::vector<uint8_t>{1} : std::vector<uint8_t>{};
    });
    return !result.empty();
}

nlohmann::json Session::stats() {
    double seconds = std::chrono::duration<double>(std::chrono::steady_clock::now() - started_).count();
    return {
        {"id", id_},
        {"core", core_ ? core_->name() : ""},
        {"connected", connected_.load()},
        {"ended", ended_.load()},
        {"encoder", video_ ? video_->name() : ""},
        {"width", video_ ? video_->width() : 0},
        {"height", video_ ? video_->height() : 0},
        {"fps", core_ ? core_->fps() : 0},
        {"framesSent", framesSent_.load()},
        {"kbpsAverage", seconds > 0 ? bytesSent_.load() * 8 / 1000.0 / seconds : 0},
        {"lastEncodeMs", lastFrameMs_.load()},
        {"lateFrames", lateFrames_.load()},
        {"inputMessages", inputMessages_.load()},
        {"buttons", core_ ? core_->pads[0].buttons.load() : 0},
    };
}

} // namespace cplay
