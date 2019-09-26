//-----------------------------------------------------------------------------
// FILE:		activity_start_local_request.go
// CONTRIBUTOR: John C Burns
// COPYRIGHT:	Copyright (c) 2016-2019 by neonFORGE, LLC.  All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

package messages

import (
	"go.uber.org/cadence/workflow"

	messagetypes "github.com/cadence-proxy/internal/messages/types"
)

type (

	// ActivityStartLocalRequest is an ActivityRequest of MessageType
	// ActivityStartLocalRequest.
	//
	// A ActivityStartLocalRequest contains a reference to a
	// ActivityRequest struct in memory and ReplyType, which is
	// the corresponding MessageType for replying to this ActivityRequest
	//
	// Starts a workflow activity.
	ActivityStartLocalRequest struct {
		*ActivityRequest
	}
)

// NewActivityStartLocalRequest is the default constructor for a ActivityStartLocalRequest
//
// returns *ActivityStartLocalRequest -> a pointer to a newly initialized ActivityStartLocalRequest
// in memory
func NewActivityStartLocalRequest() *ActivityStartLocalRequest {
	request := new(ActivityStartLocalRequest)
	request.ActivityRequest = NewActivityRequest()
	request.SetType(messagetypes.ActivityStartLocalRequest)
	request.SetReplyType(messagetypes.ActivityStartLocalReply)

	return request
}

// GetActivity gets a ActivityStartLocalRequest's Activity field
// from its properties map.  Specifies the activity to
// be executed.
//
// returns *string -> activity to execute.
func (request *ActivityStartLocalRequest) GetActivity() *string {
	return request.GetStringProperty("Activity")
}

// SetActivity sets an ActivityStartLocalRequest's Activity field
// from its properties map.  Specifies the activity to
// be executed.
//
// param value *string -> activity to execute.
func (request *ActivityStartLocalRequest) SetActivity(value *string) {
	request.SetStringProperty("Activity", value)
}

// GetArgs gets a ActivityStartLocalRequest's Args field
// from its properties map.  Optionally specifies the arguments to be passed to the activity encoded
// as a byte array.
//
// returns []byte -> []byte representing workflow activity parameters or arguments
// for executing
func (request *ActivityStartLocalRequest) GetArgs() []byte {
	return request.GetBytesProperty("Args")
}

// SetArgs sets an ActivityStartLocalRequest's Args field
// from its properties map.  Optionally specifies the arguments to be passed to the activity encoded
// as a byte array.
//
// param value []byte -> []byte representing workflow activity parameters or arguments
// for executing
func (request *ActivityStartLocalRequest) SetArgs(value []byte) {
	request.SetBytesProperty("Args", value)
}

// GetOptions gets a ActivityExecutionRequest's execution options.
//
// returns *workflow.LocalActivityOptions -> activity options.
func (request *ActivityStartLocalRequest) GetOptions() *workflow.LocalActivityOptions {
	opts := new(workflow.LocalActivityOptions)
	err := request.GetJSONProperty("Options", opts)
	if err != nil {
		return nil
	}

	return opts
}

// SetOptions sets a ActivityExecutionRequest's execution options.
//
// param value *workflow.LocalActivityOptions -> activity options.
func (request *ActivityStartLocalRequest) SetOptions(value *workflow.LocalActivityOptions) {
	request.SetJSONProperty("Options", value)
}

// GetActivityID gets the unique Id used to identify the activity.
//
// returns int64 -> the long ActivityID
func (request *ActivityStartLocalRequest) GetActivityID() int64 {
	return request.GetLongProperty("ActivityId")
}

func (request *ActivityStartLocalRequest) SetActivityID(value int64) {
	request.SetLongProperty("ActivityId", value)
}

// -------------------------------------------------------------------------
// IProxyMessage interface methods for implementing the IProxyMessage interface

// Clone inherits docs from ActivityRequest.Clone()
func (request *ActivityStartLocalRequest) Clone() IProxyMessage {
	activityStartLocalRequest := NewActivityStartLocalRequest()
	var messageClone IProxyMessage = activityStartLocalRequest
	request.CopyTo(messageClone)

	return messageClone
}

// CopyTo inherits docs from ActivityRequest.CopyTo()
func (request *ActivityStartLocalRequest) CopyTo(target IProxyMessage) {
	request.ActivityRequest.CopyTo(target)
	if v, ok := target.(*ActivityStartLocalRequest); ok {
		v.SetArgs(request.GetArgs())
		v.SetOptions(request.GetOptions())
		v.SetActivity(request.GetActivity())
		v.SetActivityID(request.GetActivityID())
	}
}
