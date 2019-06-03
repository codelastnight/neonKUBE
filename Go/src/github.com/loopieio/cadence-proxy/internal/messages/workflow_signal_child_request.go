package messages

import (
	messagetypes "github.com/loopieio/cadence-proxy/internal/messages/types"
)

type (

	// WorkflowSignalChildRequest is WorkflowRequest of MessageType
	// WorkflowSignalChildRequest.
	//
	// A WorkflowSignalChildRequest contains a reference to a
	// WorkflowRequest struct in memory and ReplyType, which is
	// the corresponding MessageType for replying to this WorkflowRequest
	//
	// Sends a signal to a child workflow.
	WorkflowSignalChildRequest struct {
		*WorkflowRequest
	}
)

// NewWorkflowSignalChildRequest is the default constructor for a WorkflowSignalChildRequest
//
// returns *WorkflowSignalChildRequest -> a reference to a newly initialized
// WorkflowSignalChildRequest in memory
func NewWorkflowSignalChildRequest() *WorkflowSignalChildRequest {
	request := new(WorkflowSignalChildRequest)
	request.WorkflowRequest = NewWorkflowRequest()
	request.SetType(messagetypes.WorkflowSignalChildRequest)
	request.SetReplyType(messagetypes.WorkflowSignalReply)

	return request
}

// GetChildID gets a WorkflowSignalChildRequest's ChildID value
// from its properties map. Identifies the child workflow.
//
// returns int64 -> long holding the value
// of a WorkflowSignalChildRequest's ChildID
func (request *WorkflowSignalChildRequest) GetChildID() int64 {
	return request.GetLongProperty("ChildId")
}

// SetChildID sets an WorkflowSignalChildRequest's ChildID value
// in its properties map. Identifies the child workflow.
//
// param value int64 -> long holding the value
// of a WorkflowSignalChildRequest's ChildID to be set in the
// WorkflowSignalChildRequest's properties map.
func (request *WorkflowSignalChildRequest) SetChildID(value int64) {
	request.SetLongProperty("ChildId", value)
}

// GetSignalName gets a WorkflowSignalChildRequest's SignalName value
// from its properties map. Identifies the signal.
//
// returns *string -> pointer to a string in memory holding the value
// of a WorkflowSignalChildRequest's SignalName
func (request *WorkflowSignalChildRequest) GetSignalName() *string {
	return request.GetStringProperty("SignalName")
}

// SetSignalName sets a WorkflowSignalChildRequest's SignalName value
// in its properties map. Identifies the signal.
//
// param value *string -> a pointer to a string in memory that holds the value
// to be set in the properties map
func (request *WorkflowSignalChildRequest) SetSignalName(value *string) {
	request.SetStringProperty("SignalName", value)
}

// GetSignalArgs gets a WorkflowSignalChildRequest's SignalArgs field
// from its properties map.  Optionally specifies the signal arguments.
//
// returns []byte -> a []byte representing the optional signal arguments
// for signaling a child workflow
func (request *WorkflowSignalChildRequest) GetSignalArgs() []byte {
	return request.GetBytesProperty("SignalArgs")
}

// SetSignalArgs sets an WorkflowSignalChildRequest's SignalArgs field
// from its properties map.  Optionally specifies the signal arguments.
//
// param value []byte -> []byte representing the optional signal arguments
// for signaling a child workflow
func (request *WorkflowSignalChildRequest) SetSignalArgs(value []byte) {
	request.SetBytesProperty("SignalArgs", value)
}

// -------------------------------------------------------------------------
// IProxyMessage interface methods for implementing the IProxyMessage interface

// Clone inherits docs from WorkflowRequest.Clone()
func (request *WorkflowSignalChildRequest) Clone() IProxyMessage {
	workflowSignalChildRequest := NewWorkflowSignalChildRequest()
	var messageClone IProxyMessage = workflowSignalChildRequest
	request.CopyTo(messageClone)

	return messageClone
}

// CopyTo inherits docs from WorkflowRequest.CopyTo()
func (request *WorkflowSignalChildRequest) CopyTo(target IProxyMessage) {
	request.WorkflowRequest.CopyTo(target)
	if v, ok := target.(*WorkflowSignalChildRequest); ok {
		v.SetChildID(request.GetChildID())
		v.SetSignalName(request.GetSignalName())
		v.SetSignalArgs(request.GetSignalArgs())
	}
}