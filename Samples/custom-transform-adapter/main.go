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

	configFilePath := os.Getenv("CONFIG_PATH")

	if configFilePath == "" {
		log.Panic("Missing config file path")
	}

	config, err := LoadConfig(configFilePath)

	if err != nil {
		log.WithField("error", err).Panic(fmt.Sprintf("Unable to load config: %s", err))
	}

	adapter := NewAdapterFromConfig(config, bridgeUrl)
	log.Fatal(adapter.ListenAndServe(port))
}
