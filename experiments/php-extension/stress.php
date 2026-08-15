<?php

$before = memory_get_usage();

for ($index = 0; $index < 100_000; $index++) {
    if (cx_greeting() !== 'Hello from CX') {
        throw new RuntimeException("Unexpected greeting at iteration {$index}");
    }

    if (cx_add($index, 1) !== $index + 1) {
        throw new RuntimeException("Unexpected sum at iteration {$index}");
    }

    if (cx_repeat('CX', 3) !== 'CXCXCX') {
        throw new RuntimeException("Unexpected repeated string at iteration {$index}");
    }

    if (cx_multiply((float)$index, 2.0) !== (float)($index * 2)) {
        throw new RuntimeException("Unexpected product at iteration {$index}");
    }

    if (cx_not(($index % 2) === 0) !== (($index % 2) !== 0)) {
        throw new RuntimeException("Unexpected boolean at iteration {$index}");
    }
}

gc_collect_cycles();
$delta = memory_get_usage() - $before;

echo "calls=100000 memory_delta={$delta}\n";
