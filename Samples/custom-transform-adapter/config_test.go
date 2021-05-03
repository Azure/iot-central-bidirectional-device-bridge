// Copyright (c) Microsoft Corporation. All rights reserved.

package main

import (
	"fmt"
	"testing"

	"github.com/stretchr/testify/assert"
)

func TestLoadConfigFileNotFound(t *testing.T) {
	_, err := LoadConfig("config_not_found.json")
	assert.EqualError(t, err, "open config_not_found.json: no such file or directory")
}

func TestLoadConfigMalformedJSON(t *testing.T) {
	_, err := LoadConfig("config_malformed_mock.json")
	assert.EqualError(t, err, "unexpected end of JSON input")
}

func ExampleLoadConfigSuccess() {
	result, _ := LoadConfig("config_mock.json")
	fmt.Println(result)
	// Output: &{[{/{id}/cde  id  key } {/message { data: .dd,  properties, componentName, creationTimeUtc }  .Device.Id  apk}]}
}

func TestValidatePathMissing(t *testing.T) {
	err := validate(&Config{D2CMessages: []D2CMessage{{Transform: "."}}})
	assert.EqualError(t, err, "Path missing in D2C message definition")
}

func TestValidateDeviceIdParamMissing(t *testing.T) {
	err := validate(&Config{D2CMessages: []D2CMessage{{Path: "/", AuthHeader: "key"}}})
	assert.EqualError(t, err, "Either deviceIdPathParam or deviceIdBodyQuery must be defined in D2C message definition /")
}

func TestValidateAuthMissing(t *testing.T) {
	err := validate(&Config{D2CMessages: []D2CMessage{{Path: "/"}}})
	assert.EqualError(t, err, "Either authHeader or authQueryParam must be defined in D2C message definition /")
}
