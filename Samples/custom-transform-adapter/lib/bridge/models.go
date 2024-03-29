package bridge

// Code generated by Microsoft (R) AutoRest Code Generator.
// Changes may cause incorrect behavior and will be lost if the code is regenerated.

import (
    "encoding/json"
    "github.com/Azure/go-autorest/autorest"
    "github.com/Azure/go-autorest/autorest/date"
)

// The package's fully qualified name.
const fqdn = "lib/bridge"

            // DeviceStatusResponseBody ...
            type DeviceStatusResponseBody struct {
            autorest.Response `json:"-"`
            Status *string `json:"status,omitempty"`
            Reason *string `json:"reason,omitempty"`
            }

            // DeviceSubscription ...
            type DeviceSubscription struct {
            autorest.Response `json:"-"`
            DeviceID *string `json:"deviceId,omitempty"`
            SubscriptionType *string `json:"subscriptionType,omitempty"`
            CallbackURL *string `json:"callbackUrl,omitempty"`
            CreatedAt *date.Time `json:"createdAt,omitempty"`
            }

            // DeviceSubscriptionWithStatus ...
            type DeviceSubscriptionWithStatus struct {
            autorest.Response `json:"-"`
            DeviceID *string `json:"deviceId,omitempty"`
            SubscriptionType *string `json:"subscriptionType,omitempty"`
            CallbackURL *string `json:"callbackUrl,omitempty"`
            CreatedAt *date.Time `json:"createdAt,omitempty"`
            Status *string `json:"status,omitempty"`
            }

            // GetTwinOKResponse ...
            type GetTwinOKResponse struct {
            autorest.Response `json:"-"`
            Twin *GetTwinOKResponseTwin `json:"twin,omitempty"`
            }

            // GetTwinOKResponseTwin ...
            type GetTwinOKResponseTwin struct {
            Properties *GetTwinOKResponseTwinProperties `json:"properties,omitempty"`
            }

            // GetTwinOKResponseTwinProperties ...
            type GetTwinOKResponseTwinProperties struct {
            Desired interface{} `json:"desired,omitempty"`
            Reported interface{} `json:"reported,omitempty"`
            }

            // MessageBody ...
            type MessageBody struct {
            Data map[string]interface{} `json:"data"`
            Properties map[string]*string `json:"properties"`
            ComponentName *string `json:"componentName,omitempty"`
            CreationTimeUtc *date.Time `json:"creationTimeUtc,omitempty"`
            }

        // MarshalJSON is the custom marshaler for MessageBody.
        func (mb MessageBody)MarshalJSON() ([]byte, error){
        objectMap := make(map[string]interface{})
                if(mb.Data != nil) {
                objectMap["data"] = mb.Data
                }
                if(mb.Properties != nil) {
                objectMap["properties"] = mb.Properties
                }
                if(mb.ComponentName != nil) {
                objectMap["componentName"] = mb.ComponentName
                }
                if(mb.CreationTimeUtc != nil) {
                objectMap["creationTimeUtc"] = mb.CreationTimeUtc
                }
                return json.Marshal(objectMap)
        }

            // RegistrationBody ...
            type RegistrationBody struct {
            ModelID *string `json:"modelId,omitempty"`
            }

            // ReportedPropertiesPatch ...
            type ReportedPropertiesPatch struct {
            Patch map[string]interface{} `json:"patch"`
            }

        // MarshalJSON is the custom marshaler for ReportedPropertiesPatch.
        func (rpp ReportedPropertiesPatch)MarshalJSON() ([]byte, error){
        objectMap := make(map[string]interface{})
                if(rpp.Patch != nil) {
                objectMap["patch"] = rpp.Patch
                }
                return json.Marshal(objectMap)
        }

            // SubscriptionCreateOrUpdateBody ...
            type SubscriptionCreateOrUpdateBody struct {
            CallbackURL *string `json:"callbackUrl,omitempty"`
            }

