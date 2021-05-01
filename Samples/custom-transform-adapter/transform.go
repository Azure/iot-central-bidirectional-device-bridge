// Copyright (c) Microsoft Corporation. All rights reserved.

package main

import (
	"errors"
	"fmt"

	"github.com/itchyny/gojq"
)

// Keeps a set of pre-compiled jq queries ready for execution
type TransformEngine struct {
	transforms map[string]*gojq.Code
}

func NewTransformEngine() *TransformEngine {
	return &TransformEngine{make(map[string]*gojq.Code)}
}

// Saves a query, identified by Id, for later execution
func (engine *TransformEngine) AddTransform(id string, query string) error {
	parsed, err := gojq.Parse(query)

	if err != nil {
		return err
	}

	compiled, err := gojq.Compile(parsed)

	if err != nil {
		return err
	}

	engine.transforms[id] = compiled

	return nil
}

// Executes the transformation identified by Id over the given input.
//
// Thread safe.
func (engine *TransformEngine) Execute(id string, input map[string]interface{}) (interface{}, error) {
	compiled, ok := engine.transforms[id]

	if !ok {
		return nil, errors.New(fmt.Sprintf("Transformation for id %s not found", id))
	}

	iter := compiled.Run(input)
	result, ok := iter.Next()

	if !ok {
		return nil, errors.New(fmt.Sprintf("Transform id %s generated empty result", id))
	}

	if err, ok := result.(error); ok {
		return nil, errors.New(fmt.Sprintf("Transform id %s failed: %s", id, err))
	}

	if _, ok := iter.Next(); ok {
		return nil, errors.New(fmt.Sprintf("Transform id %s generated multiple results", id))
	}

	return result, nil
}
