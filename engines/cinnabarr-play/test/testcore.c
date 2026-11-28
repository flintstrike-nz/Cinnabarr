/*
 * A tiny libretro core for testing cinnabarr-play without a real game.
 *
 * 320x240 at 60 fps: a dark blue screen with a bar sweeping across it, which turns white while any joypad button
 * is held (so a page can measure the time from pressing a button to seeing the change), and a 440 Hz tone.
 * It saves its frame counter as its state and counts presses in "battery" save RAM.
 */
#include <math.h>
#include <stdint.h>
#include <string.h>

#include "libretro.h"

#define WIDTH 320
#define HEIGHT 240
#define FPS 60.0
#define RATE 48000.0
#define SAMPLES_PER_FRAME 800 /* RATE / FPS */

static retro_environment_t environ_cb;
static retro_video_refresh_t video_cb;
static retro_audio_sample_batch_t audio_batch_cb;
static retro_input_poll_t input_poll_cb;
static retro_input_state_t input_state_cb;

static uint32_t pixels[WIDTH * HEIGHT];
static int16_t samples[SAMPLES_PER_FRAME * 2];
static uint64_t frame;
static double phase;
static uint8_t save_ram[64];
static int was_pressed;

RETRO_API void retro_set_environment(retro_environment_t cb) {
    bool no_game = true;
    environ_cb = cb;
    cb(RETRO_ENVIRONMENT_SET_SUPPORT_NO_GAME, &no_game);
}
RETRO_API void retro_set_video_refresh(retro_video_refresh_t cb) { video_cb = cb; }
RETRO_API void retro_set_audio_sample(retro_audio_sample_t cb) { (void)cb; }
RETRO_API void retro_set_audio_sample_batch(retro_audio_sample_batch_t cb) { audio_batch_cb = cb; }
RETRO_API void retro_set_input_poll(retro_input_poll_t cb) { input_poll_cb = cb; }
RETRO_API void retro_set_input_state(retro_input_state_t cb) { input_state_cb = cb; }

RETRO_API void retro_init(void) { frame = 0; }
RETRO_API void retro_deinit(void) {}
RETRO_API unsigned retro_api_version(void) { return RETRO_API_VERSION; }

RETRO_API void retro_get_system_info(struct retro_system_info *info) {
    memset(info, 0, sizeof(*info));
    info->library_name = "Cinnabarr test core";
    info->library_version = "1";
    info->valid_extensions = "test|bin";
    info->need_fullpath = false;
}

RETRO_API void retro_get_system_av_info(struct retro_system_av_info *info) {
    memset(info, 0, sizeof(*info));
    info->geometry.base_width = WIDTH;
    info->geometry.base_height = HEIGHT;
    info->geometry.max_width = WIDTH;
    info->geometry.max_height = HEIGHT;
    info->geometry.aspect_ratio = 4.0f / 3.0f;
    info->timing.fps = FPS;
    info->timing.sample_rate = RATE;
}

RETRO_API void retro_set_controller_port_device(unsigned port, unsigned device) { (void)port; (void)device; }
RETRO_API void retro_reset(void) { frame = 0; }

RETRO_API void retro_run(void) {
    int pressed;
    uint32_t background;
    int bar;
    int x, y, i;

    input_poll_cb();
    pressed = input_state_cb(0, RETRO_DEVICE_JOYPAD, 0, RETRO_DEVICE_ID_JOYPAD_MASK) != 0;
    if (pressed && !was_pressed) {
        save_ram[0]++;
    }
    was_pressed = pressed;

    background = pressed ? 0x00FFFFFFu : 0x00102060u;
    bar = (int)(frame * 4 % WIDTH);
    for (y = 0; y < HEIGHT; y++) {
        for (x = 0; x < WIDTH; x++) {
            pixels[y * WIDTH + x] = (x >= bar && x < bar + 16 && y > HEIGHT * 3 / 4) ? 0x00E34234u : background;
        }
    }
    video_cb(pixels, WIDTH, HEIGHT, WIDTH * sizeof(uint32_t));

    for (i = 0; i < SAMPLES_PER_FRAME; i++) {
        int16_t s = (int16_t)(sin(phase) * 3000);
        samples[i * 2] = s;
        samples[i * 2 + 1] = s;
        phase += 2 * 3.14159265358979 * 440.0 / RATE;
    }
    audio_batch_cb(samples, SAMPLES_PER_FRAME);
    frame++;
}

RETRO_API size_t retro_serialize_size(void) { return sizeof(frame); }
RETRO_API bool retro_serialize(void *data, size_t size) {
    if (size < sizeof(frame)) {
        return false;
    }
    memcpy(data, &frame, sizeof(frame));
    return true;
}
RETRO_API bool retro_unserialize(const void *data, size_t size) {
    if (size < sizeof(frame)) {
        return false;
    }
    memcpy(&frame, data, sizeof(frame));
    return true;
}
RETRO_API void retro_cheat_reset(void) {}
RETRO_API void retro_cheat_set(unsigned index, bool enabled, const char *code) { (void)index; (void)enabled; (void)code; }

RETRO_API bool retro_load_game(const struct retro_game_info *game) {
    enum retro_pixel_format format = RETRO_PIXEL_FORMAT_XRGB8888;
    (void)game;
    return environ_cb(RETRO_ENVIRONMENT_SET_PIXEL_FORMAT, &format);
}
RETRO_API bool retro_load_game_special(unsigned type, const struct retro_game_info *info, size_t num) {
    (void)type; (void)info; (void)num;
    return false;
}
RETRO_API void retro_unload_game(void) {}
RETRO_API unsigned retro_get_region(void) { return RETRO_REGION_NTSC; }
RETRO_API void *retro_get_memory_data(unsigned id) { return id == RETRO_MEMORY_SAVE_RAM ? save_ram : NULL; }
RETRO_API size_t retro_get_memory_size(unsigned id) { return id == RETRO_MEMORY_SAVE_RAM ? sizeof(save_ram) : 0; }
