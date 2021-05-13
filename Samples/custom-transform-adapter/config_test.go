// Copyright (c) Microsoft Corporation. All rights reserved.

package main

import (
	"fmt"
	"os"
	"testing"

	"github.com/stretchr/testify/assert"
)

func TestLoadConfigFileNotFound(t *testing.T) {
	_, err := LoadConfig(".", "config_not_found.json")
	assert.EqualError(t, err, "open config_not_found.json: no such file or directory")
}

func TestLoadConfigMalformedJSON(t *testing.T) {
	_, err := LoadConfig(".", "config_malformed_mock.json")
	assert.EqualError(t, err, "unexpected end of JSON input")
}

func ExampleLoadConfigSuccess() {
	currentPath, _ := os.Getwd()
	result, _ := LoadConfig(currentPath, "config_mock.json")
	fmt.Println(result)
	// Output: &{[{/{id}/cde  id  key } {/message { data: .dd,  properties, componentName, creationTimeUtc }  .Device.Id  apk} {/telemetry/{deviceId} {
	//     data: .obj
	//         | map( { (.name | tostring): .value } )
	//         | add
	// } deviceId  api-key }]}
}

func TestValidatePathMissing(t *testing.T) {
	err := validate(&ConfigRaw{D2CMessages: []D2CMessageRaw{{Transform: "."}}})
	assert.EqualError(t, err, "Path missing in D2C message definition")
}

func TestValidateMultipleTransforms(t *testing.T) {
	err := validate(&ConfigRaw{D2CMessages: []D2CMessageRaw{{Path: "/", Transform: ".", TransformFile: "./transform.jq"}}})
	assert.EqualError(t, err, "Either transform or transformFile may be defined, not both, in D2C message definition /")
}

func TestValidateDeviceIdParamMissing(t *testing.T) {
	err := validate(&ConfigRaw{D2CMessages: []D2CMessageRaw{{Path: "/", AuthHeader: "key"}}})
	assert.EqualError(t, err, "Either deviceIdPathParam or deviceIdBodyQuery must be defined in D2C message definition /")
}

func TestValidateAuthMissing(t *testing.T) {
	err := validate(&ConfigRaw{D2CMessages: []D2CMessageRaw{{Path: "/"}}})
	assert.EqualError(t, err, "Either authHeader or authQueryParam must be defined in D2C message definition /")
}
