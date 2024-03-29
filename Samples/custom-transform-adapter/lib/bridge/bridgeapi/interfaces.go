package bridgeapi

// Code generated by Microsoft (R) AutoRest Code Generator.
// Changes may cause incorrect behavior and will be lost if the code is regenerated.

import (
	"context"

	"github.com/Azure/go-autorest/autorest"
	"github.com/iot-for-all/iotc-device-bridge/custom-transform-adapter/lib/bridge"
)

// BaseClientAPI contains the set of methods on the BaseClient type.
type BaseClientAPI interface {
	CreateOrUpdateC2DMessageSubscription(ctx context.Context, deviceID string, body *bridge.SubscriptionCreateOrUpdateBody) (result bridge.DeviceSubscriptionWithStatus, err error)
	CreateOrUpdateConnectionStatusSubscription(ctx context.Context, deviceID string, body *bridge.SubscriptionCreateOrUpdateBody) (result bridge.DeviceSubscription, err error)
	CreateOrUpdateDesiredPropertiesSubscription(ctx context.Context, deviceID string, body *bridge.SubscriptionCreateOrUpdateBody) (result bridge.DeviceSubscriptionWithStatus, err error)
	CreateOrUpdateMethodsSubscription(ctx context.Context, deviceID string, body *bridge.SubscriptionCreateOrUpdateBody) (result bridge.DeviceSubscriptionWithStatus, err error)
	DeleteC2DMessageSubscription(ctx context.Context, deviceID string) (result autorest.Response, err error)
	DeleteConnectionStatusSubscription(ctx context.Context, deviceID string) (result autorest.Response, err error)
	DeleteDesiredPropertiesSubscription(ctx context.Context, deviceID string) (result autorest.Response, err error)
	DeleteMethodsSubscription(ctx context.Context, deviceID string) (result autorest.Response, err error)
	GetC2DMessageSubscription(ctx context.Context, deviceID string) (result bridge.DeviceSubscriptionWithStatus, err error)
	GetConnectionStatusSubscription(ctx context.Context, deviceID string) (result bridge.DeviceSubscription, err error)
	GetCurrentConnectionStatus(ctx context.Context, deviceID string) (result bridge.DeviceStatusResponseBody, err error)
	GetDesiredPropertiesSubscription(ctx context.Context, deviceID string) (result bridge.DeviceSubscriptionWithStatus, err error)
	GetMethodsSubscription(ctx context.Context, deviceID string) (result bridge.DeviceSubscriptionWithStatus, err error)
	GetTwin(ctx context.Context, deviceID string) (result bridge.GetTwinOKResponse, err error)
	Register(ctx context.Context, deviceID string, body *bridge.RegistrationBody) (result autorest.Response, err error)
	Resync(ctx context.Context, deviceID string) (result autorest.Response, err error)
	SendMessage(ctx context.Context, deviceID string, body *bridge.MessageBody) (result autorest.Response, err error)
	UpdateReportedProperties(ctx context.Context, deviceID string, body *bridge.ReportedPropertiesPatch) (result autorest.Response, err error)
}

var _ BaseClientAPI = (*bridge.BaseClient)(nil)
