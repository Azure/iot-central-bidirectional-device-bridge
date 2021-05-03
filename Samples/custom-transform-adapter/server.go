// Copyright (c) Microsoft Corporation. All rights reserved.

package main

import (
	"context"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"math/rand"
	"net/http"
	"time"

	"github.com/Azure/go-autorest/autorest"
	"github.com/Azure/go-autorest/autorest/date"
	"github.com/google/uuid"
	"github.com/gorilla/mux"
	"github.com/iot-for-all/iotc-device-bridge/custom-transform-adapter/lib/bridge"
	"github.com/mitchellh/mapstructure"
	log "github.com/sirupsen/logrus"
)

const maxBodySize = 1024 * 1024 // 1 MiB

type BridgeClient interface {
	SetAuthorizer(autorest.Authorizer)
	SetRetryAttempts(int)
	SendMessage(context.Context, string, *bridge.MessageBody) (autorest.Response, error)
	GetBaseURI() string
}

type BridgeClientAutorest struct {
	bridge.BaseClient
}

func (client *BridgeClientAutorest) SetAuthorizer(authorizer autorest.Authorizer) {
	client.Authorizer = authorizer
}

func (client *BridgeClientAutorest) SetRetryAttempts(retryAttempts int) {
	client.RetryAttempts = retryAttempts
}

func (client *BridgeClientAutorest) SendMessage(ctx context.Context, deviceID string, body *bridge.MessageBody) (autorest.Response, error) {
	return client.BaseClient.SendMessage(ctx, deviceID, body)
}

func (client *BridgeClientAutorest) GetBaseURI() string {
	return client.BaseURI
}

type Adapter struct {
	GetBridgeClient func() BridgeClient
	Router          *mux.Router
	Engine          *TransformEngine
}

// D2C message route definition augmented to include the Id of the cached transform queries.
type AugmentedD2CMessage struct {
	D2CMessage
	TransformId         string
	DeviceIdBodyQueryId string
}

// Builds a transform adapter for a given configuration.
func NewAdapterFromConfig(config *Config, bridgeEndpoint string) *Adapter {
	log.Info(fmt.Sprintf("Initializing adapter for Bridge %s", bridgeEndpoint))

	adapter := Adapter{
		Engine: NewTransformEngine(),
		Router: mux.NewRouter(),
		GetBridgeClient: func() BridgeClient {
			return &BridgeClientAutorest{bridge.NewWithBaseURI(bridgeEndpoint)}
		},
	}

	for _, message := range config.D2CMessages {
		log.Info(fmt.Sprintf("Initializing route %s", message.Path))
		augmentedMessage := AugmentedD2CMessage{D2CMessage: message}

		// Initialize cache for request body transform.
		if message.Transform != "" {
			augmentedMessage.TransformId = uuid.New().String()
			if err := adapter.Engine.AddTransform(augmentedMessage.TransformId, message.Transform); err != nil {
				log.WithField("error", err).Panic(fmt.Sprintf("Failed to add request body transform for route %s: %s", message.Path, err))
			}
		} else {
			log.Warn(fmt.Sprintf("Empty transform. Route %s will be set as pass-through", message.Path))
		}

		// Initialize cache for device Id transform.
		if message.DeviceIdBodyQuery != "" {
			augmentedMessage.DeviceIdBodyQueryId = uuid.New().String()
			if err := adapter.Engine.AddTransform(augmentedMessage.DeviceIdBodyQueryId, message.DeviceIdBodyQuery); err != nil {
				log.WithField("error", err).Panic(fmt.Sprintf("Failed to add device Id query transform for route %s: %s", message.Path, err))
			}
		}

		handler := adapter.buildD2CMessageHandler(augmentedMessage)
		adapter.Router.HandleFunc(message.Path, withLogging(handler)).Methods("POST")
	}

	return &adapter
}

func (adapter *Adapter) ListenAndServe(port int) error {
	log.Info(fmt.Sprintf("Server listening on port %d", port))
	return http.ListenAndServe(fmt.Sprintf(":%d", port), adapter.Router)
}

// Builds the HTTP handler for a given C2D route definition.
func (adapter *Adapter) buildD2CMessageHandler(message AugmentedD2CMessage) func(*log.Entry, http.ResponseWriter, *http.Request) {
	return func(logger *log.Entry, w http.ResponseWriter, r *http.Request) {
		var jsonBody map[string]interface{}
		if err := decodeJsonBody(w, r, &jsonBody); err != nil {
			respondError(logger, w, http.StatusBadRequest, fmt.Sprintf("Failed to decode JSON body: %s", err.Error()))
			return
		}

		// Execute body transformation if one was provided. If not, the route is pass-through.
		var transformedPayload interface{}
		if message.TransformId != "" {
			var err error
			transformedPayload, err = adapter.Engine.Execute(message.TransformId, jsonBody)

			if err != nil {
				respondError(logger, w, http.StatusBadRequest, fmt.Sprintf("Payload transformation failed: %s", err.Error()))
				return
			}
		} else {
			transformedPayload = jsonBody
		}

		if err := decodeDateTimeField(&transformedPayload, "creationTimeUtc"); err != nil {
			respondError(logger, w, http.StatusBadRequest, fmt.Sprintf("Failed to parse \"creationTimeUtc\": %s", err.Error()))
			return
		}

		// Convert transformation output to Autorest typed input.
		var bridgePayload bridge.MessageBody
		if err := mapstructure.Decode(transformedPayload, &bridgePayload); err != nil {
			respondError(logger, w, http.StatusBadRequest, fmt.Sprintf("Failed to transform payload to expected Device Bridge format: %s", err.Error()))
			return
		}

		bridgeClient := adapter.GetBridgeClient()

		// Extracts the API key from the query parameter or header.
		var apiKey string
		if message.AuthQueryParam != "" {
			values, ok := r.URL.Query()[message.AuthQueryParam]

			if !ok || len(values) < 1 {
				respondError(logger, w, http.StatusBadRequest, fmt.Sprintf("Expected auth query parameter \"%s\" to be defined", message.AuthQueryParam))
				return
			}

			apiKey = values[0]
		} else if message.AuthHeader != "" {
			apiKey = r.Header.Get(message.AuthHeader)
		} else {
			respondError(logger, w, http.StatusBadRequest, "No auth method specified")
			return
		}

		bridgeClient.SetAuthorizer(autorest.NewAPIKeyAuthorizerWithHeaders(map[string]interface{}{
			"x-api-key": apiKey,
		}))

		var deviceId string
		if message.DeviceIdBodyQueryId != "" {
			var queriedDeviceId interface{}
			var err error
			if queriedDeviceId, err = adapter.Engine.Execute(message.DeviceIdBodyQueryId, jsonBody); err != nil {
				respondError(logger, w, http.StatusBadRequest, fmt.Sprintf("Device Id body query failed: %s", err.Error()))
				return
			}

			var ok bool
			if deviceId, ok = queriedDeviceId.(string); !ok || deviceId == "" {
				respondError(logger, w, http.StatusBadRequest, "Expected result from device Id body query to be string")
				return
			}
		} else if message.DeviceIdPathParam != "" {
			var ok bool
			if deviceId, ok = mux.Vars(r)[message.DeviceIdPathParam]; !ok {
				respondError(logger, w, http.StatusBadRequest, fmt.Sprintf("Expected device Id in \"%s\" path parameter", message.DeviceIdPathParam))
				return
			}
		} else {
			respondError(logger, w, http.StatusBadRequest, "No device Id specified")
			return
		}

		bridgeClient.SetRetryAttempts(1) // Don't retry (the Bridge already has internal retries)

		if bridgeResponse, err := bridgeClient.SendMessage(r.Context(), deviceId, &bridgePayload); err != nil {
			// We return the Bridge status code if we have it
			var responseStatusCode int
			if bridgeResponse != (autorest.Response{}) {
				responseStatusCode = bridgeResponse.StatusCode
			} else {
				responseStatusCode = http.StatusInternalServerError
			}

			respondError(logger, w, responseStatusCode, fmt.Sprintf("Call to Device Bridge failed: %s", err.Error()))
			return
		}

		w.WriteHeader(http.StatusOK)
	}
}

// An HTTP response writer extended to capture the response status.
type LoggingResponseWriter struct {
	http.ResponseWriter
	ResponseStatus int
}

func (r *LoggingResponseWriter) WriteHeader(status int) {
	r.ResponseStatus = status
	r.ResponseWriter.WriteHeader(status)
}

// Wraps a request handler, logging the request, response, and injecting a logger with request context.
func withLogging(handler func(*log.Entry, http.ResponseWriter, *http.Request)) func(w http.ResponseWriter, r *http.Request) {
	return func(w http.ResponseWriter, r *http.Request) {
		startTime := time.Now()
		logger := log.WithField("request_id", makeShortId())
		logger.Info(fmt.Sprintf("HTTP request. Path %s", r.URL.Path))
		loggingResponseWriter := &LoggingResponseWriter{ResponseWriter: w}
		handler(logger, loggingResponseWriter, r)
		duration := time.Since(startTime).String()
		logger.Info(fmt.Sprintf("HTTP response. Path %s, status %d, duration %s", r.URL.Path, loggingResponseWriter.ResponseStatus, duration))
	}
}

func decodeJsonBody(w http.ResponseWriter, r *http.Request, output *map[string]interface{}) error {
	r.Body = http.MaxBytesReader(w, r.Body, maxBodySize)
	decoder := json.NewDecoder(r.Body)
	return decoder.Decode(output)
}

func respondError(logger *log.Entry, w http.ResponseWriter, statusCode int, message string) {
	logger.Error(message)
	respondJson(logger, w, statusCode, map[string]string{"error": message})
}

func respondJson(logger *log.Entry, w http.ResponseWriter, statusCode int, payload interface{}) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(statusCode)
	if err := json.NewEncoder(w).Encode(payload); err != nil {
		log.WithField("error", err).Error(fmt.Sprintf("Failed to encode JSON response: %s", err))
	}
}

// Returns a random 8-character string.
func makeShortId() string {
	randBytes := make([]byte, 4)
	rand.Read(randBytes)
	return hex.EncodeToString(randBytes)
}

// Converts the specified string field of a JSON map into a date time value, using the Autorest date.Time type.
// The value is decoded in place. Ignores if field is not present in the map.
func decodeDateTimeField(json *interface{}, field string) error {
	if jsonMap, ok := (*json).(map[string]interface{}); ok {
		if fieldRaw, ok := jsonMap[field]; ok && fieldRaw != nil {
			if fieldStr, ok := fieldRaw.(string); ok {
				dateTime, err := time.Parse(time.RFC3339, fieldStr)
				if err != nil {
					return err
				}

				jsonMap[field] = date.Time{dateTime}
			} else {
				return errors.New(fmt.Sprintf("if provided, field \"%s\" must be a timestamp string", field))
			}
		}
	}

	return nil
}
