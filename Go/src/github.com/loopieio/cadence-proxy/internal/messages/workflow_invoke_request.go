package messages

import (
	"time"

	messagetypes "github.com/loopieio/cadence-proxy/internal/messages/types"
)

type (

	// WorkflowInvokeRequest is WorkflowContextRequest of MessageType
	// WorkflowInvokeRequest.
	//
	// A WorkflowInvokeRequest contains a RequestId and a reference to a
	// WorkflowContextRequest struct in memory and ReplyType, which is
	// the corresponding MessageType for replying to this WorkflowContextRequest
	//
	// A WorkflowInvokeRequest will pass all of the given information
	// necessary to invoke a cadence workflow via the cadence client
	WorkflowInvokeRequest struct {
		*WorkflowContextRequest
	}
)

// NewWorkflowInvokeRequest is the default constructor for a WorkflowInvokeRequest
//
// returns *WorkflowInvokeRequest -> a reference to a newly initialized
// WorkflowInvokeRequest in memory
func NewWorkflowInvokeRequest() *WorkflowInvokeRequest {
	request := new(WorkflowInvokeRequest)
	request.WorkflowContextRequest = NewWorkflowContextRequest()
	request.Type = messagetypes.WorkflowInvokeRequest
	request.SetReplyType(messagetypes.WorkflowInvokeReply)

	return request
}

// GetName gets a WorkflowInvokeRequest's Name value
// from its properties map
//
// returns *string -> pointer to a string in memory holding the value
// of a WorkflowInvokeRequest's Name
func (request *WorkflowInvokeRequest) GetName() *string {
	return request.GetStringProperty("Name")
}

// SetName sets a WorkflowInvokeRequest's Name value
// in its properties map
//
// param value *string -> a pointer to a string in memory that holds the value
// to be set in the properties map
func (request *WorkflowInvokeRequest) SetName(value *string) {
	request.SetStringProperty("Name", value)
}

// GetArgs gets a WorkflowInvokeRequest's Args field
// from its properties map.  Args is a []byte holding the arguments
// for invoking a specific workflow
//
// returns []byte -> a []byte of representing workflow parameters
// or arguments for invoking
func (request *WorkflowInvokeRequest) GetArgs() []byte {
	return request.GetBytesProperty("Args")
}

// SetArgs sets an WorkflowInvokeRequest's Args field
// from its properties map.  Args is a []byte holding the arguments
// for invoking a specific workflow
//
// param value []byte -> []byte of representing workflow parameters
// or arguments for invoking
func (request *WorkflowInvokeRequest) SetArgs(value []byte) {
	request.SetBytesProperty("Args", value)
}

// -------------------------------------------------------------------------
// IProxyMessage interface methods for implementing the IProxyMessage interface

// Clone inherits docs from WorkflowContextRequest.Clone()
func (request *WorkflowInvokeRequest) Clone() IProxyMessage {
	workflowInvokeRequest := NewWorkflowInvokeRequest()
	var messageClone IProxyMessage = workflowInvokeRequest
	request.CopyTo(messageClone)

	return messageClone
}

// CopyTo inherits docs from WorkflowContextRequest.CopyTo()
func (request *WorkflowInvokeRequest) CopyTo(target IProxyMessage) {
	request.WorkflowContextRequest.CopyTo(target)
	if v, ok := target.(*WorkflowInvokeRequest); ok {
		v.SetName(request.GetName())
		v.SetArgs(request.GetArgs())
	}
}

// SetProxyMessage inherits docs from WorkflowContextRequest.SetProxyMessage()
func (request *WorkflowInvokeRequest) SetProxyMessage(value *ProxyMessage) {
	request.WorkflowContextRequest.SetProxyMessage(value)
}

// GetProxyMessage inherits docs from WorkflowContextRequest.GetProxyMessage()
func (request *WorkflowInvokeRequest) GetProxyMessage() *ProxyMessage {
	return request.WorkflowContextRequest.GetProxyMessage()
}

// GetRequestID inherits docs from WorkflowContextRequest.GetRequestID()
func (request *WorkflowInvokeRequest) GetRequestID() int64 {
	return request.WorkflowContextRequest.GetRequestID()
}

// SetRequestID inherits docs from WorkflowContextRequest.SetRequestID()
func (request *WorkflowInvokeRequest) SetRequestID(value int64) {
	request.WorkflowContextRequest.SetRequestID(value)
}

// -------------------------------------------------------------------------
// IProxyRequest interface methods for implementing the IProxyRequest interface

// GetReplyType inherits docs from WorkflowContextRequest.GetReplyType()
func (request *WorkflowInvokeRequest) GetReplyType() messagetypes.MessageType {
	return request.WorkflowContextRequest.GetReplyType()
}

// SetReplyType inherits docs from WorkflowContextRequest.SetReplyType()
func (request *WorkflowInvokeRequest) SetReplyType(value messagetypes.MessageType) {
	request.WorkflowContextRequest.SetReplyType(value)
}

// GetTimeout inherits docs from WorkflowContextRequest.GetTimeout()
func (request *WorkflowInvokeRequest) GetTimeout() time.Duration {
	return request.WorkflowContextRequest.GetTimeout()
}

// SetTimeout inherits docs from WorkflowContextRequest.SetTimeout()
func (request *WorkflowInvokeRequest) SetTimeout(value time.Duration) {
	request.WorkflowContextRequest.SetTimeout(value)
}

// -------------------------------------------------------------------------
// IWorkflowContextRequest interface methods for implementing the IWorkflowContextRequest interface

// GetWorkflowContextID inherits docs from WorkflowContextRequest.GetWorkflowContextID()
func (request *WorkflowInvokeRequest) GetWorkflowContextID() int64 {
	return request.WorkflowContextRequest.GetWorkflowContextID()
}

// SetWorkflowContextID inherits docs from WorkflowContextRequest.SetWorkflowContextID()
func (request *WorkflowInvokeRequest) SetWorkflowContextID(value int64) {
	request.WorkflowContextRequest.SetWorkflowContextID(value)
}
