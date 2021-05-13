// Copyright (c) Microsoft Corporation. All rights reserved.

package main

import (
	"fmt"
	"os"
	"strconv"

	log "github.com/sirupsen/logrus"
)

func init() {
	log.SetFormatter(&log.JSONFormatter{})
}

func main() {
	port, err := strconv.Atoi(os.Getenv("PORT"))

	if err != nil {
		log.WithField("error", err).Panic(fmt.Sprintf("Invalid port: %s", err))
	}

	bridgeUrl := os.Getenv("BRIDGE_URL")

	if bridgeUrl == "" {
		log.Panic("Missing Bridge URL")
	}

	configPath := os.Getenv("CONFIG_PATH")
	configFileName := os.Getenv("CONFIG_FILE")

	if configPath == "" {
		log.Panic("Missing config path")
	}

	if configFileName == "" {
		log.Panic("Missing config file")
	}

	config, err := LoadConfig(configPath, configFileName)

	if err != nil {
		log.WithField("error", err).Panic(fmt.Sprintf("Unable to load config: %s", err))
	}

	adapter := NewAdapterFromConfig(config, bridgeUrl)
	log.Fatal(adapter.ListenAndServe(port))
}
