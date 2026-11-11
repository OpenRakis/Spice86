#include <stdio.h>
#include <stdlib.h>

static unsigned long fold_words(const unsigned short *words, unsigned count) {
    unsigned i;
    unsigned long total = 0;
    for (i = 0; i < count; i++) {
        total = total * 2UL + words[i & 3u];
    }
    return total;
}

static short halve(short value) {
    return value / 2;
}

int main(int argc, char **argv) {
    static const unsigned short words[4] = { 3, 5, 7, 11 };
    unsigned count;
    unsigned long total;
    if (argc != 2) {
        return 2;
    }
    count = (unsigned)atoi(argv[1]);
    if (count > 32u) {
        return 3;
    }
    total = fold_words(words, count);
    printf("%lu %lu %d %u %u\n", total, total / 10UL,
        (int)halve((short)-(int)count), (unsigned)!count,
        (unsigned)(count >= 1u && count <= 9u));
    return 0;
}
