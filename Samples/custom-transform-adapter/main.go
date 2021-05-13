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
		log.WithField("error", err).Panic(fmt.Sprintf("invalid port: %s", err))
	}

	bridgeUrl := os.Getenv("BRIDGE_URL")

	if bridgeUrl == "" {
		log.Panic("missing Bridge URL")
	}

	configPath := os.Getenv("CONFIG_PATH")
	configFileName := os.Getenv("CONFIG_FILE")

	if configPath == "" {
		log.Panic("missing config path")
	}

	if configFileName == "" {
		log.Panic("missing config file")
	}

	config, err := LoadConfig(configPath, configFileName)

	if err != nil {
		log.WithField("error", err).Panic(fmt.Sprintf("unable to load config: %s", err))
	}

	adapter := NewAdapterFromConfig(config, bridgeUrl)
	log.Fatal(adapter.ListenAndServe(port))
}
