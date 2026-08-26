/*
 * Feeds this repository's deterministic byte stream to a TestU01 battery.
 *
 * TestU01's batteries want a generator object, not a file, so the stream arrives on stdin as
 * little-endian 32-bit words and is handed over through unif01_CreateExternGenBits. That is the
 * same stream RandomQuality emits for PractRand, so a generator cannot pass one battery and fail
 * the other because of how its bytes were produced.
 *
 * A battery is a fixed-length experiment: it decides in advance how many numbers it will draw.
 * Running out of input therefore means the caller asked for too few bytes, and that is a harness
 * fault rather than a statistical result -- so it exits 3 with the count it managed, instead of
 * recycling the stream and reporting a verdict about data it invented.
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "bbattery.h"
#include "unif01.h"

static unsigned long consumed = 0;

static unsigned int NextBits(void)
{
    unsigned int value;
    if (fread(&value, sizeof(value), 1, stdin) != 1)
    {
        fprintf(
            stderr,
            "wallstop-testu01: input stream exhausted after %lu words; supply more --bytes\n",
            consumed);
        exit(3);
    }
    consumed++;
    return value;
}

int main(int argc, char **argv)
{
    const char *battery = argc > 1 ? argv[1] : "SmallCrush";
    unif01_Gen *gen = unif01_CreateExternGenBits("wallstop-stream", NextBits);

    if (strcmp(battery, "SmallCrush") == 0)
    {
        bbattery_SmallCrush(gen);
    }
    else if (strcmp(battery, "Crush") == 0)
    {
        bbattery_Crush(gen);
    }
    else if (strcmp(battery, "BigCrush") == 0)
    {
        bbattery_BigCrush(gen);
    }
    else
    {
        fprintf(stderr, "wallstop-testu01: unknown battery '%s'\n", battery);
        return 2;
    }

    fprintf(stderr, "wallstop-testu01: consumed %lu words\n", consumed);
    unif01_DeleteExternGenBits(gen);
    return 0;
}
