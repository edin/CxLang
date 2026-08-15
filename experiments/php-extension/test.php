<?php

if (!extension_loaded('cx_demo')) {
    throw new RuntimeException('cx_demo extension is not loaded');
}

$answer = cx_answer();
if ($answer !== 42) {
    throw new RuntimeException("Expected cx_answer() to return 42, got {$answer}");
}

$greeting = cx_greeting();
if ($greeting !== 'Hello from CX') {
    throw new RuntimeException("Unexpected greeting: {$greeting}");
}

if (cx_add(20, 22) !== 42) {
    throw new RuntimeException('Expected cx_add(20, 22) to return 42');
}

if (cx_optional_sum(10) !== 13
    || cx_optional_sum(10, 20) !== 32
    || cx_optional_sum(10, 20, 30) !== 60) {
    throw new RuntimeException('Unexpected cx_optional_sum() result');
}

$optionalSumParameters = (new ReflectionFunction('cx_optional_sum'))->getParameters();
if (count($optionalSumParameters) !== 3
    || $optionalSumParameters[0]->isOptional()
    || !$optionalSumParameters[1]->isOptional()
    || $optionalSumParameters[1]->getDefaultValue() !== 1
    || !$optionalSumParameters[2]->isOptional()
    || $optionalSumParameters[2]->getDefaultValue() !== 2) {
    throw new RuntimeException('Unexpected cx_optional_sum() optional parameter metadata');
}

$reflection = new ReflectionFunction('cx_add');
if ((string) $reflection->getReturnType() !== 'int') {
    throw new RuntimeException('Expected cx_add() to declare an int return type');
}

$parameters = $reflection->getParameters();
if (count($parameters) !== 2
    || $parameters[0]->getName() !== 'left'
    || (string) $parameters[0]->getType() !== 'int'
    || $parameters[1]->getName() !== 'right'
    || (string) $parameters[1]->getType() !== 'int') {
    throw new RuntimeException('Unexpected cx_add() parameter metadata');
}

try {
    cx_add(1);
    throw new RuntimeException('cx_add() accepted too few arguments');
} catch (ArgumentCountError) {
}

try {
    cx_add(1, 2, 3);
    throw new RuntimeException('cx_add() accepted too many arguments');
} catch (ArgumentCountError) {
}

try {
    cx_add('not an integer', 1);
    throw new RuntimeException('cx_add() accepted an invalid argument type');
} catch (TypeError) {
}

if (cx_repeat('CX', 3) !== 'CXCXCX'
    || cx_repeat('CX') !== 'CX'
    || cx_repeat('', 10) !== ''
    || cx_repeat('CX', 0) !== '') {
    throw new RuntimeException('Unexpected cx_repeat() result');
}

if (cx_multiply(6.0, 7.0) !== 42.0) {
    throw new RuntimeException('Expected cx_multiply(6.0, 7.0) to return 42.0');
}

$multiplyReflection = new ReflectionFunction('cx_multiply');
if ((string) $multiplyReflection->getReturnType() !== 'float'
    || (string) $multiplyReflection->getParameters()[0]->getType() !== 'float'
    || (string) $multiplyReflection->getParameters()[1]->getType() !== 'float') {
    throw new RuntimeException('Unexpected cx_multiply() metadata');
}

if (cx_not(true) !== false || cx_not(false) !== true) {
    throw new RuntimeException('Unexpected cx_not() result');
}

if (cx_weighted(6, 0.5, true) !== 3.0
    || cx_weighted(6, 0.5, false) !== 0.0) {
    throw new RuntimeException('Unexpected cx_weighted() result');
}

$notReflection = new ReflectionFunction('cx_not');
if ((string) $notReflection->getReturnType() !== 'bool'
    || (string) $notReflection->getParameters()[0]->getType() !== 'bool') {
    throw new RuntimeException('Unexpected cx_not() metadata');
}

$repeatReflection = new ReflectionFunction('cx_repeat');
if ((string) $repeatReflection->getReturnType() !== 'string') {
    throw new RuntimeException('Expected cx_repeat() to declare a string return type');
}

$repeatParameters = $repeatReflection->getParameters();
if (count($repeatParameters) !== 2
    || $repeatParameters[0]->getName() !== 'value'
    || (string) $repeatParameters[0]->getType() !== 'string'
    || $repeatParameters[1]->getName() !== 'count'
    || (string) $repeatParameters[1]->getType() !== 'int') {
    throw new RuntimeException('Unexpected cx_repeat() parameter metadata');
}

if (!$repeatParameters[1]->isOptional()
    || $repeatParameters[1]->getDefaultValue() !== 1) {
    throw new RuntimeException('Expected cx_repeat() count to default to 1');
}

try {
    cx_repeat('CX', -1);
    throw new RuntimeException('cx_repeat() accepted a negative count');
} catch (ValueError) {
}

try {
    cx_repeat([], 1);
    throw new RuntimeException('cx_repeat() accepted an invalid value type');
} catch (TypeError) {
}

try {
    cx_multiply([], 1.0);
    throw new RuntimeException('cx_multiply() accepted an invalid argument type');
} catch (TypeError) {
}

try {
    cx_not([]);
    throw new RuntimeException('cx_not() accepted an invalid argument type');
} catch (TypeError) {
}

echo "cx_demo smoke test passed\n";
