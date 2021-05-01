package main

import (
	"bytes"
	"context"
	"errors"
	"fmt"
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/Azure/go-autorest/autorest"
	"github.com/iot-for-all/iotc-device-bridge/custom-transform-adapter/lib/bridge"
	"github.com/stretchr/testify/assert"
)

type BridgeClientMock struct {
	LastSendMessageBody     *bridge.MessageBody
	LastSendMessageDeviceId string
	LastAuthorizer          autorest.Authorizer
}

func (client *BridgeClientMock) SetAuthorizer(authorizer autorest.Authorizer) {
	client.LastAuthorizer = authorizer
}

func (client *BridgeClientMock) SetRetryAttempts(retryAttempts int) {
}

func (client *BridgeClientMock) SendMessage(ctx context.Context, deviceID string, body *bridge.MessageBody) (autorest.Response, error) {
	client.LastSendMessageBody = body
	client.LastSendMessageDeviceId = deviceID
	return autorest.Response{}, nil
}

func (client *BridgeClientMock) GetBaseURI() string {
	return "test"
}

var mockBridgeClient = BridgeClientMock{}

var mockGetBridgeClient = func() BridgeClient {
	return &mockBridgeClient
}

type BridgeWithBrokenSend struct {
	BridgeClientMock
	err     error
	respose autorest.Response
}

func (client *BridgeWithBrokenSend) SendMessage(ctx context.Context, deviceID string, body *bridge.MessageBody) (autorest.Response, error) {
	return client.respose, client.err
}

func TestNewAdapterFromConfigBridgeUrl(t *testing.T) {
	adapter := NewAdapterFromConfig(&Config{D2CMessages: []D2CMessage{
		{
			Path:              "/{id}/message",
			DeviceIdPathParam: "id",
			AuthHeader:        "key",
		},
	}}, "localhost:1000")
	assert.Equal(t, "localhost:1000", adapter.GetBridgeClient().GetBaseURI())
}

func TestMalformedJsonBody(t *testing.T) {
	adapter := NewAdapterFromConfig(&Config{D2CMessages: []D2CMessage{
		{
			Path:              "/{id}/message",
			DeviceIdPathParam: "id",
			AuthHeader:        "key",
		},
	}}, "localhost:1000")
	adapter.GetBridgeClient = mockGetBridgeClient
	var jsonBody = []byte("not a JSON")
	req, _ := http.NewRequest("POST", "/test_device/message", bytes.NewBuffer(jsonBody))
	req.Header.Add("key", "test_key")
	recorder := httptest.NewRecorder()
	adapter.Router.ServeHTTP(recorder, req)
	assert.Equal(t, 400, recorder.Code)
	assert.Contains(t, recorder.Body.String(), "Failed to decode JSON body")
}

func TestBasicTransform(t *testing.T) {
	adapter := NewAdapterFromConfig(&Config{D2CMessages: []D2CMessage{
		{
			Path:              "/{id}/message",
			DeviceIdPathParam: "id",
			AuthHeader:        "key",
			Transform:         "{ data: .telemetry }",
		},
	}}, "localhost:1000")

	adapter.GetBridgeClient = mockGetBridgeClient
	var jsonBody = []byte(`{ "telemetry": {"temperature": 21} }`)
	req, _ := http.NewRequest("POST", "/test_device/message", bytes.NewBuffer(jsonBody))
	req.Header.Add("key", "test_key")
	recorder := httptest.NewRecorder()
	adapter.Router.ServeHTTP(recorder, req)
	assert.Equal(t, 200, recorder.Code)
	assert.Equal(t, "test_device", mockBridgeClient.LastSendMessageDeviceId)
	assert.Equal(t, float64(21), mockBridgeClient.LastSendMessageBody.Data["temperature"])
}

func TestBadTransform(t *testing.T) {
	adapter := NewAdapterFromConfig(&Config{D2CMessages: []D2CMessage{
		{
			Path:              "/{id}/message",
			DeviceIdPathParam: "id",
			AuthHeader:        "key",
			Transform:         "{(.a): 1}",
		},
	}}, "localhost:1000")

	adapter.GetBridgeClient = mockGetBridgeClient
	var jsonBody = []byte(`{ "a": 1 }`)
	req, _ := http.NewRequest("POST", "/test_device/message", bytes.NewBuffer(jsonBody))
	req.Header.Add("key", "test_key")
	recorder := httptest.NewRecorder()
	adapter.Router.ServeHTTP(recorder, req)
	assert.Equal(t, 400, recorder.Code)
	assert.Contains(t, recorder.Body.String(), "Payload transformation failed")
}

func TestPassthroughTransform(t *testing.T) {
	adapter := NewAdapterFromConfig(&Config{D2CMessages: []D2CMessage{
		{
			Path:              "/{id}/message",
			DeviceIdPathParam: "id",
			AuthHeader:        "key",
		},
	}}, "localhost:1000")

	adapter.GetBridgeClient = mockGetBridgeClient
	var jsonBody = []byte(`{ "data": {"temperature": 30} }`)
	req, _ := http.NewRequest("POST", "/test_device_passthrough/message", bytes.NewBuffer(jsonBody))
	req.Header.Add("key", "test_key")
	recorder := httptest.NewRecorder()
	adapter.Router.ServeHTTP(recorder, req)
	assert.Equal(t, 200, recorder.Code)
	assert.Equal(t, "test_device_passthrough", mockBridgeClient.LastSendMessageDeviceId)
	assert.Equal(t, float64(30), mockBridgeClient.LastSendMessageBody.Data["temperature"])
}

func TestCreationTimeUtc(t *testing.T) {
	adapter := NewAdapterFromConfig(&Config{D2CMessages: []D2CMessage{
		{
			Path:              "/{id}/message",
			DeviceIdPathParam: "id",
			AuthHeader:        "key",
			Transform:         "{ data: .telemetry, creationTimeUtc: .time }",
		},
	}}, "localhost:1000")

	adapter.GetBridgeClient = mockGetBridgeClient
	var jsonBody = []byte(`{ "telemetry": {"temperature": 22}, "time": "2031-09-22T12:42:31Z" }`)
	req, _ := http.NewRequest("POST", "/test_device_time/message", bytes.NewBuffer(jsonBody))
	req.Header.Add("key", "test_key")
	recorder := httptest.NewRecorder()
	adapter.Router.ServeHTTP(recorder, req)
	assert.Equal(t, 200, recorder.Code)
	assert.Equal(t, "test_device_time", mockBridgeClient.LastSendMessageDeviceId)
	assert.Equal(t, float64(22), mockBridgeClient.LastSendMessageBody.Data["temperature"])
	assert.Equal(t, "2031-09-22T12:42:31Z", mockBridgeClient.LastSendMessageBody.CreationTimeUtc.String())
}

func TestBadCreationTimeUtc(t *testing.T) {
	adapter := NewAdapterFromConfig(&Config{D2CMessages: []D2CMessage{
		{
			Path:              "/{id}/message",
			DeviceIdPathParam: "id",
			AuthHeader:        "key",
			Transform:         "{ data: .telemetry, creationTimeUtc: .time }",
		},
	}}, "localhost:1000")

	adapter.GetBridgeClient = mockGetBridgeClient
	var jsonBody = []byte(`{ "telemetry": {"temperature": 22}, "time": "abc" }`)
	req, _ := http.NewRequest("POST", "/test_device_time/message", bytes.NewBuffer(jsonBody))
	req.Header.Add("key", "test_key")
	recorder := httptest.NewRecorder()
	adapter.Router.ServeHTTP(recorder, req)
	assert.Equal(t, 400, recorder.Code)
	assert.Contains(t, recorder.Body.String(), "Failed to parse \\\"creationTimeUtc\\\"")
}

func TestBadOutputPayload(t *testing.T) {
	adapter := NewAdapterFromConfig(&Config{D2CMessages: []D2CMessage{
		{
			Path:              "/{id}/message",
			DeviceIdPathParam: "id",
			AuthHeader:        "key",
			Transform:         "{ data: .telemetry }",
		},
	}}, "localhost:1000")

	adapter.GetBridgeClient = mockGetBridgeClient
	var jsonBody = []byte(`{ "telemetry": "bad data" }`)
	req, _ := http.NewRequest("POST", "/test_device_payload/message", bytes.NewBuffer(jsonBody))
	req.Header.Add("key", "test_key")
	recorder := httptest.NewRecorder()
	adapter.Router.ServeHTTP(recorder, req)
	assert.Equal(t, 400, recorder.Code)
	assert.Contains(t, recorder.Body.String(), "Failed to transform payload to expected Device Bridge format")
}

func ExampleAuthQueryParam() {
	adapter := NewAdapterFromConfig(&Config{D2CMessages: []D2CMessage{
		{
			Path:              "/{id}/message",
			DeviceIdPathParam: "id",
			AuthQueryParam:    "key",
		},
	}}, "localhost:1000")

	adapter.GetBridgeClient = mockGetBridgeClient
	var jsonBody = []byte(`{ }`)
	req, _ := http.NewRequest("POST", "/test_device/message?key=my_key", bytes.NewBuffer(jsonBody))
	recorder := httptest.NewRecorder()
	adapter.Router.ServeHTTP(recorder, req)

	fmt.Println(recorder.Code, mockBridgeClient.LastAuthorizer)
	// Output: 200 &{map[x-api-key:my_key] map[]}
}

func TestBadAuthQueryParam(t *testing.T) {
	adapter := NewAdapterFromConfig(&Config{D2CMessages: []D2CMessage{
		{
			Path:              "/{id}/message",
			DeviceIdPathParam: "id",
			AuthQueryParam:    "key",
		},
	}}, "localhost:1000")

	adapter.GetBridgeClient = mockGetBridgeClient
	var jsonBody = []byte(`{ }`)
	req, _ := http.NewRequest("POST", "/test_device/message", bytes.NewBuffer(jsonBody))
	recorder := httptest.NewRecorder()
	adapter.Router.ServeHTTP(recorder, req)
	assert.Equal(t, 400, recorder.Code)
	assert.Contains(t, recorder.Body.String(), "Expected auth query parameter \\\"key\\\" to be defined")
}

func ExampleAuthHeader() {
	adapter := NewAdapterFromConfig(&Config{D2CMessages: []D2CMessage{
		{
			Path:              "/{id}/message",
			DeviceIdPathParam: "id",
			AuthHeader:        "key",
		},
	}}, "localhost:1000")

	adapter.GetBridgeClient = mockGetBridgeClient
	var jsonBody = []byte(`{ }`)
	req, _ := http.NewRequest("POST", "/test_device/message", bytes.NewBuffer(jsonBody))
	req.Header.Add("key", "my_header_key")
	recorder := httptest.NewRecorder()
	adapter.Router.ServeHTTP(recorder, req)
	fmt.Println(recorder.Code, mockBridgeClient.LastAuthorizer)
	// Output: 200 &{map[x-api-key:my_header_key] map[]}
}

func TestNoAuth(t *testing.T) {
	adapter := NewAdapterFromConfig(&Config{D2CMessages: []D2CMessage{
		{
			Path:              "/{id}/message",
			DeviceIdPathParam: "id",
		},
	}}, "localhost:1000")

	adapter.GetBridgeClient = mockGetBridgeClient
	var jsonBody = []byte(`{ }`)
	req, _ := http.NewRequest("POST", "/test_device/message", bytes.NewBuffer(jsonBody))
	recorder := httptest.NewRecorder()
	adapter.Router.ServeHTTP(recorder, req)
	assert.Equal(t, 400, recorder.Code)
	assert.Contains(t, recorder.Body.String(), "No auth method specified")
}

func TestDeviceIdBodyField(t *testing.T) {
	adapter := NewAdapterFromConfig(&Config{D2CMessages: []D2CMessage{
		{
			Path:              "/message",
			DeviceIdBodyField: "body_field",
			AuthHeader:        "key",
		},
	}}, "localhost:1000")

	adapter.GetBridgeClient = mockGetBridgeClient
	var jsonBody = []byte(`{ "body_field": "body_id" }`)
	req, _ := http.NewRequest("POST", "/message", bytes.NewBuffer(jsonBody))
	req.Header.Add("key", "test_key")
	recorder := httptest.NewRecorder()
	adapter.Router.ServeHTTP(recorder, req)
	assert.Equal(t, 200, recorder.Code)
	assert.Equal(t, "body_id", mockBridgeClient.LastSendMessageDeviceId)
}

func TestDeviceIdBodyFieldMissing(t *testing.T) {
	adapter := NewAdapterFromConfig(&Config{D2CMessages: []D2CMessage{
		{
			Path:              "/message",
			DeviceIdBodyField: "body_field",
			AuthHeader:        "key",
		},
	}}, "localhost:1000")

	adapter.GetBridgeClient = mockGetBridgeClient
	var jsonBody = []byte(`{ }`)
	req, _ := http.NewRequest("POST", "/message", bytes.NewBuffer(jsonBody))
	req.Header.Add("key", "test_key")
	recorder := httptest.NewRecorder()
	adapter.Router.ServeHTTP(recorder, req)
	assert.Equal(t, 400, recorder.Code)
	assert.Contains(t, recorder.Body.String(), "Expected device Id in \\\"body_field\\\" body field")
}

func TestDeviceIdBodyFieldBadFormat(t *testing.T) {
	adapter := NewAdapterFromConfig(&Config{D2CMessages: []D2CMessage{
		{
			Path:              "/message",
			DeviceIdBodyField: "body_field",
			AuthHeader:        "key",
		},
	}}, "localhost:1000")

	adapter.GetBridgeClient = mockGetBridgeClient
	var jsonBody = []byte(`{ "body_field": 123 }`)
	req, _ := http.NewRequest("POST", "/message", bytes.NewBuffer(jsonBody))
	req.Header.Add("key", "test_key")
	recorder := httptest.NewRecorder()
	adapter.Router.ServeHTTP(recorder, req)
	assert.Equal(t, 400, recorder.Code)
	assert.Contains(t, recorder.Body.String(), "Expected device Id in \\\"body_field\\\" body field to be string")
}

func TestDeviceIdPathParameterMissing(t *testing.T) {
	adapter := NewAdapterFromConfig(&Config{D2CMessages: []D2CMessage{
		{
			Path:       "/{another_id}/message",
			AuthHeader: "key",
		},
	}}, "localhost:1000")

	adapter.GetBridgeClient = mockGetBridgeClient
	var jsonBody = []byte(`{ }`)
	req, _ := http.NewRequest("POST", "/test_device/message", bytes.NewBuffer(jsonBody))
	req.Header.Add("key", "test_key")
	recorder := httptest.NewRecorder()
	adapter.Router.ServeHTTP(recorder, req)
	assert.Equal(t, 400, recorder.Code)
	assert.Contains(t, recorder.Body.String(), "No device Id specified")
}

func TestBridgeStatusCode(t *testing.T) {
	adapter := NewAdapterFromConfig(&Config{D2CMessages: []D2CMessage{
		{
			Path:              "/{id}/message",
			DeviceIdPathParam: "id",
			AuthHeader:        "key",
		},
	}}, "localhost:1000")

	var brokenBridgeClient = BridgeWithBrokenSend{err: errors.New("bad request"), respose: autorest.Response{&http.Response{StatusCode: 401}}}

	var mockGetBrokenBridgeClient = func() BridgeClient {
		return &brokenBridgeClient
	}

	adapter.GetBridgeClient = mockGetBrokenBridgeClient
	var jsonBody = []byte(`{ }`)
	req, _ := http.NewRequest("POST", "/test_device/message", bytes.NewBuffer(jsonBody))
	req.Header.Add("key", "test_key")
	recorder := httptest.NewRecorder()
	adapter.Router.ServeHTTP(recorder, req)
	assert.Equal(t, 401, recorder.Code)
	assert.Contains(t, recorder.Body.String(), "Call to Device Bridge failed: bad request")
}

func TestBridgeEmptyStatusCodeReturns500(t *testing.T) {
	adapter := NewAdapterFromConfig(&Config{D2CMessages: []D2CMessage{
		{
			Path:              "/{id}/message",
			DeviceIdPathParam: "id",
			AuthHeader:        "key",
		},
	}}, "localhost:1000")

	var brokenBridgeClient = BridgeWithBrokenSend{err: errors.New("bad request"), respose: autorest.Response{}}

	var mockGetBrokenBridgeClient = func() BridgeClient {
		return &brokenBridgeClient
	}

	adapter.GetBridgeClient = mockGetBrokenBridgeClient
	var jsonBody = []byte(`{ }`)
	req, _ := http.NewRequest("POST", "/test_device/message", bytes.NewBuffer(jsonBody))
	req.Header.Add("key", "test_key")
	recorder := httptest.NewRecorder()
	adapter.Router.ServeHTTP(recorder, req)
	assert.Equal(t, 500, recorder.Code)
	assert.Contains(t, recorder.Body.String(), "Call to Device Bridge failed: bad request")
}

func Test404(t *testing.T) {
	adapter := NewAdapterFromConfig(&Config{D2CMessages: []D2CMessage{
		{
			Path:              "/{id}/message",
			DeviceIdPathParam: "id",
			AuthHeader:        "key",
		},
	}}, "localhost:1000")

	adapter.GetBridgeClient = mockGetBridgeClient
	var jsonBody = []byte(`{ }`)
	req, _ := http.NewRequest("POST", "/anotherpath", bytes.NewBuffer(jsonBody))
	req.Header.Add("key", "test_key")
	recorder := httptest.NewRecorder()
	adapter.Router.ServeHTTP(recorder, req)
	assert.Equal(t, 404, recorder.Code)
}

func TestMultipleRoutes(t *testing.T) {
	adapter := NewAdapterFromConfig(&Config{D2CMessages: []D2CMessage{
		{
			Path:              "/{id}/message",
			DeviceIdPathParam: "id",
			AuthHeader:        "key",
			Transform:         "{ data: .telemetry }",
		},
		{
			Path:              "/another_message",
			DeviceIdBodyField: "body_field",
			AuthHeader:        "another_key",
		},
	}}, "localhost:1000")

	adapter.GetBridgeClient = mockGetBridgeClient
	var jsonBody = []byte(`{ "telemetry": {"temperature": 21} }`)
	req, _ := http.NewRequest("POST", "/test_device/message", bytes.NewBuffer(jsonBody))
	req.Header.Add("key", "test_key")
	recorder := httptest.NewRecorder()
	adapter.Router.ServeHTTP(recorder, req)
	assert.Equal(t, 200, recorder.Code)
	assert.Equal(t, "test_device", mockBridgeClient.LastSendMessageDeviceId)
	assert.Equal(t, float64(21), mockBridgeClient.LastSendMessageBody.Data["temperature"])

	jsonBody = []byte(`{ "body_field": "body_id", "data": {"humidity": 30}}`)
	req, _ = http.NewRequest("POST", "/another_message", bytes.NewBuffer(jsonBody))
	req.Header.Add("another_key", "test_key")
	recorder = httptest.NewRecorder()
	adapter.Router.ServeHTTP(recorder, req)
	assert.Equal(t, 200, recorder.Code)
	assert.Equal(t, "body_id", mockBridgeClient.LastSendMessageDeviceId)
	assert.Equal(t, float64(30), mockBridgeClient.LastSendMessageBody.Data["humidity"])
}
