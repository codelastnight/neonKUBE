// Code generated by thriftrw --generate-plugin-api
// @generated

// Copyright (c) 2018 Uber Technologies, Inc.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

package api

import (
	"go.uber.org/thriftrw/internal/envelope"
	"go.uber.org/thriftrw/wire"
)

// ServiceGeneratorHandler serves an implementation of the ServiceGenerator service.
type ServiceGeneratorHandler struct {
	impl ServiceGenerator
}

// NewServiceGeneratorHandler builds a new ServiceGenerator handler.
func NewServiceGeneratorHandler(service ServiceGenerator) ServiceGeneratorHandler {
	return ServiceGeneratorHandler{
		impl: service,
	}
}

// Handle receives and handles a request for the ServiceGenerator service.
func (h ServiceGeneratorHandler) Handle(name string, reqValue wire.Value) (wire.Value, error) {
	switch name {

	case "generate":

		var args ServiceGenerator_Generate_Args
		if err := args.FromWire(reqValue); err != nil {
			return wire.Value{}, err
		}

		result, err := ServiceGenerator_Generate_Helper.WrapResponse(
			h.impl.Generate(args.Request),
		)
		if err != nil {
			return wire.Value{}, err
		}

		return result.ToWire()

	default:

		return wire.Value{}, envelope.ErrUnknownMethod(name)

	}
}
