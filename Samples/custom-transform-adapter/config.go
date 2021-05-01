// Copyright (c) Microsoft Corporation. All rights reserved.

package main

import (
	"encoding/json"
	"errors"
	"fmt"
	"io/ioutil"
)

// Represents an adapter configuration (with routes, transforms, etc.)
type Config struct {
	D2CMessages []D2CMessage `json:"d2cMessages"`
}

type D2CMessage struct {
	Path              string `json:"path"`              // Path filter for requests that will be routed to this transform
	Transform         string `json:"transform"`         // jq transform query
	DeviceIdPathParam string `json:"deviceIdPathParam"` // Path parameter containing device Id
	DeviceIdBodyField string `json:"deviceIdBodyField"` // Body field containing device Id
	AuthHeader        string `json:"authHeader"`        // Header containing auth key
	AuthQueryParam    string `json:"authQueryParam"`    // Query parameter containing auth key
}

// Loads, parses, and validates an adapter config from a file.
func LoadConfig(configFileName string) (*Config, error) {
	configFile, err := ioutil.ReadFile(configFileName)

	if err != nil {
		return nil, err
	}

	var config Config

	if err = json.Unmarshal(configFile, &config); err != nil {
		return nil, err
	}

	if err := validate(&config); err != nil {
		return nil, err
	}

	return &config, nil
}

func validate(config *Config) error {
	for _, message := range config.D2CMessages {
		if message.Path == "" {
			return errors.New("Path missing in D2C message definition")
		}

		if message.AuthHeader == "" && message.AuthQueryParam == "" {
			return errors.New(fmt.Sprintf("Either authHeader or authQueryParam must be defined in D2C message definition %s", message.Path))
		}

		if message.DeviceIdPathParam == "" && message.DeviceIdBodyField == "" {
			return errors.New(fmt.Sprintf("Either deviceIdPathParam or deviceIdBodyField must be defined in D2C message definition %s", message.Path))
		}
	}

	return nil
}
