// Copyright (c) Microsoft Corporation. All rights reserved.

package main

import (
	"fmt"
	"testing"

	"github.com/stretchr/testify/assert"
)

func TestTransformEngineAddTransform(t *testing.T) {
	engine := NewTransformEngine()
	assert.NoError(t, engine.AddTransform("pass-through", "."))
	assert.NoError(t, engine.AddTransform("sample", ". | {a, b}"))
	assert.Error(t, engine.AddTransform("invalid", ".{a, b}"))
}

// Simple query that maps { "a": 1 } to { "b": 1}.
func ExampleTransformEngineExecuteSample() {
	engine := NewTransformEngine()
	engine.AddTransform("sample", "{ b: .a}")
	result, _ := engine.Execute("sample", map[string]interface{}{"a": 1})
	fmt.Println(result)
	// Output: map[b:1]
}

// Simple multi-line query that maps { "a": 1 } to { "b": 1}.
func ExampleTransformEngineExecuteMultilineQuery() {
	engine := NewTransformEngine()
	engine.AddTransform("sample", `{
		b: .a
	}`)
	result, _ := engine.Execute("sample", map[string]interface{}{"a": 1})
	fmt.Println(result)
	// Output: map[b:1]
}

func TestTransformEngineExecuteNotFound(t *testing.T) {
	engine := NewTransformEngine()
	assert.NoError(t, engine.AddTransform("a-transform", "."))
	_, err := engine.Execute("another-transform", map[string]interface{}{})
	assert.EqualError(t, err, "transform-adapter: transformation for id another-transform not found")
}

func TestTransformEngineExecuteFail(t *testing.T) {
	engine := NewTransformEngine()
	assert.NoError(t, engine.AddTransform("bad-transform", "{(.a): 1}"))
	_, err := engine.Execute("bad-transform", map[string]interface{}{"a": 1})
	assert.EqualError(t, err, "transform-adapter: transform id bad-transform failed: expected a string for object key but got: number (1)")
}

func TestTransformEngineExecuteMultipleResults(t *testing.T) {
	engine := NewTransformEngine()
	assert.NoError(t, engine.AddTransform("multiple-results", "{a: 1},{b: 2}"))
	_, err := engine.Execute("multiple-results", map[string]interface{}{})
	assert.EqualError(t, err, "transform-adapter: transform id multiple-results generated multiple results")
}
