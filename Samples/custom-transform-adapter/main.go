// Copyright (c) Microsoft Corporation. All rights reserved.

package main

import (
	"os"

	log "github.com/sirupsen/logrus"
)

func init() {
	log.SetFormatter(&log.JSONFormatter{})
}

func main() {
	bridgeUrl := os.Getenv("BRIDGE_URL")
	configPath := os.Getenv("CONFIG_PATH")
	configFileName := os.Getenv("CONFIG_FILE")

	config, err := LoadConfig(configPath, configFileName)

	if err != nil {
		log.WithField("error", err).Panicf("unable to load config: %s", err)
	}

	adapter, err := NewAdapter(config, bridgeUrl)

	if err != nil {
		log.WithField("error", err).Panicf("unable to build adapter server: %s", err)
	}

	log.Fatal(adapter.ListenAndServe(os.Getenv("PORT")))
}
