// Copyright (c) Microsoft Corporation. All rights reserved.

package main

import (
	"encoding/json"
	"errors"
	"fmt"
	"io/ioutil"
	"path/filepath"
)

// Config represents an adapter configuration (with routes, transforms, etc.)
type Config struct {
	D2CMessages []D2CMessage
}

type D2CMessage struct {
	Path              string // Path filter for requests that will be routed to this transform
	Transform         string // jq query to tranform the request body
	DeviceIdPathParam string // Path parameter containing device Id
	DeviceIdBodyQuery string // jq query to pick the device Id from the request body
	AuthHeader        string // Header containing auth key
	AuthQueryParam    string // Query parameter containing auth key
}

// ConfigRaw represents the input config file, before processing.
type ConfigRaw struct {
	D2CMessages []D2CMessageRaw `json:"d2cMessages"`
}

type D2CMessageRaw struct {
	Path              string `json:"path"`
	Transform         string `json:"transform"`
	TransformFile     string `json:"transformFile"`
	DeviceIdPathParam string `json:"deviceIdPathParam"`
	DeviceIdBodyQuery string `json:"deviceIdBodyQuery"`
	AuthHeader        string `json:"authHeader"`
	AuthQueryParam    string `json:"authQueryParam"`
}

// LoadConfig loads, parses, and validates an adapter config from a file.
func LoadConfig(configPath string, configFileName string) (*Config, error) {
	configFile, err := ioutil.ReadFile(filepath.Join(configPath, configFileName))

	if err != nil {
		return nil, err
	}

	var configRaw ConfigRaw

	if err = json.Unmarshal(configFile, &configRaw); err != nil {
		return nil, err
	}

	if err := validate(&configRaw); err != nil {
		return nil, err
	}

	config := Config{D2CMessages: make([]D2CMessage, len(configRaw.D2CMessages))}

	// Generate processed config
	for i, message := range configRaw.D2CMessages {
		// Resolve transform files
		if message.TransformFile != "" {
			transformFileContent, err := ioutil.ReadFile(filepath.Join(configPath, message.TransformFile))

			if err != nil {
				return nil, err
			}

			message.Transform = string(transformFileContent)
		}

		config.D2CMessages[i] = D2CMessage{
			Path:              message.Path,
			Transform:         message.Transform,
			DeviceIdPathParam: message.DeviceIdPathParam,
			DeviceIdBodyQuery: message.DeviceIdBodyQuery,
			AuthHeader:        message.AuthHeader,
			AuthQueryParam:    message.AuthQueryParam,
		}
	}

	return &config, nil
}

func validate(config *ConfigRaw) error {
	for _, message := range config.D2CMessages {
		if message.Path == "" {
			return errors.New("transform-adapter: path missing in D2C message definition")
		}

		if message.Transform != "" && message.TransformFile != "" {
			return fmt.Errorf("transform-adapter: either transform or transformFile may be defined, not both, in D2C message definition %s", message.Path)
		}

		if (message.AuthHeader == "" && message.AuthQueryParam == "") || (message.AuthHeader != "" && message.AuthQueryParam != "") {
			return fmt.Errorf("transform-adapter: either authHeader or authQueryParam must be defined in D2C message definition %s", message.Path)
		}

		if (message.DeviceIdPathParam == "" && message.DeviceIdBodyQuery == "") || (message.DeviceIdPathParam != "" && message.DeviceIdBodyQuery != "") {
			return fmt.Errorf("transform-adapter: either deviceIdPathParam or deviceIdBodyQuery must be defined in D2C message definition %s", message.Path)
		}
	}

	return nil
}
