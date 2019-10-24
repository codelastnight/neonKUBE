// Copyright (c) 2019 Uber Technologies, Inc.
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

// Package pluginapigen provides a plugin Handle that generates code used by the
// plugin system itself.
//
// This is made available as a hidden flag: "--generate-plugin-api".
package pluginapigen

import (
	"path/filepath"
	"strings"

	intplugin "go.uber.org/thriftrw/internal/plugin"
	"go.uber.org/thriftrw/plugin"
	"go.uber.org/thriftrw/plugin/api"
)

// Handle is a plugin.Handle that generates code for the plugin system API.
var Handle intplugin.Handle = handle{}

type handle struct{}

func (handle) Name() string {
	return "pluginapigen"
}

func (handle) Close() error {
	return nil // no-op
}

func (handle) ServiceGenerator() intplugin.ServiceGenerator {
	return sgen{}
}

type sgen struct{}

func (sgen) Handle() intplugin.Handle {
	return Handle
}

func (sgen) Generate(req *api.GenerateServiceRequest) (*api.GenerateServiceResponse, error) {
	files := make(map[string][]byte)
	for _, serviceID := range req.RootServices {
		service := req.Services[serviceID]
		module := req.Modules[service.ModuleID]

		templateData := struct {
			Service *api.Service
			Request *api.GenerateServiceRequest
		}{Service: service, Request: req}

		var (
			err  error
			opts []plugin.TemplateOption
		)

		opts = append(opts, templateOptions...)
		opts = append(opts, plugin.GoFileImportPath(module.ImportPath))

		ifacePath := filepath.Join(module.Directory, strings.ToLower(service.Name)+".go")
		files[ifacePath], err = plugin.GoFileFromTemplate(
			ifacePath, interfaceTemplate, templateData, opts...)
		if err != nil {
			return nil, err
		}

		clientPath := filepath.Join(module.Directory, strings.ToLower(service.Name)+"_client.go")
		files[clientPath], err = plugin.GoFileFromTemplate(
			clientPath, clientTemplate, templateData, opts...)
		if err != nil {
			return nil, err
		}

		handlerPath := filepath.Join(module.Directory, strings.ToLower(service.Name)+"_handler.go")
		files[handlerPath], err = plugin.GoFileFromTemplate(
			handlerPath, handlerTemplate, templateData, opts...)
		if err != nil {
			return nil, err
		}
	}
	return &api.GenerateServiceResponse{Files: files}, nil
}

// convenience function because "index .Request.Services .Service.ParentID"
// doesn't work. Index expects a ServiceID, not *ServiceID.
func getService(req *api.GenerateServiceRequest, id api.ServiceID) *api.Service {
	return req.Services[id]
}

var templateOptions = []plugin.TemplateOption{
	plugin.TemplateFunc("basename", filepath.Base),
	plugin.TemplateFunc("getService", getService),
}

const interfaceTemplate = `
// Code generated by thriftrw --generate-plugin-api
// @generated

<$module := index .Request.Modules .Service.ModuleID>
package <basename $module.ImportPath>

type <.Service.Name> interface {
	<if .Service.ParentID>
		<$parent := getService .Request .Service.ParentID>
		<if eq $parent.ModuleID .Service.ModuleID>
			<$parent.Name>
		<else>
			<$parentModule := index .Request.Modules $parent.ModuleID>
			<import $parentModule.ImportPath>.<$parent.Name>
		<end>
	<end>

	<range .Service.Functions>
		<.Name>(<range .Arguments>
			<.Name> <formatType .Type>,<end>
		) <if .ReturnType>(<formatType .ReturnType>, error)<else>error<end>
	<end>
}
`

const clientTemplate = `
// Code generated by thriftrw --generate-plugin-api
// @generated

<$module := index .Request.Modules .Service.ModuleID>
package <basename $module.ImportPath>

<$envelope := import "go.uber.org/thriftrw/internal/envelope">

<$client := printf "_%s_client" .Service.Name>

// Client implements a <.Service.Name> client.
type <$client> struct {
	<if .Service.ParentID>
		<$parent := getService .Request .Service.ParentID>
		<if eq $parent.ModuleID .Service.ModuleID>
			<$parent.Name>
		<else>
			<$parentModule := index .Request.Modules $parent.ModuleID>
			<import $parentModule.ImportPath>.<$parent.Name>
		<end>
	<end>

	client <$envelope>.Client
}

// New<.Service.Name>Client builds a new <.Service.Name> client.
func New<.Service.Name>Client(c <$envelope>.Client) <.Service.Name> {
	return &<$client>{
		client: c,
		<if .Service.ParentID>
			<$parent := getService .Request .Service.ParentID>
			<if eq $parent.ModuleID .Service.ModuleID>
				<$parent.Name>: New<$parent.Name>Client(t),
			<else>
				<$parentModule := index .Request.Modules $parent.ModuleID>
				<$parent.Name>: <import $parentModule>.New<$parent.Name>Client(t),
			<end>
		<end>
	}
}

<$serviceName := .Service.Name>
<range .Service.Functions>
<$wire := import "go.uber.org/thriftrw/wire">
<$prefix := printf "%s_%s_" $serviceName .Name>

func (c *<$client>) <.Name>(<range .Arguments>
	_<.Name> <formatType .Type>,<end>
) (<if .ReturnType>success <formatType .ReturnType>,<end> err error) {
	args := <$prefix>Helper.Args(<range .Arguments>_<.Name>, <end>)

	var body <$wire>.Value
	body, err = args.ToWire()
	if err != nil {
		return
	}

	body, err = c.client.Send("<.ThriftName>", body)
	if err != nil {
		return
	}

	var result <$prefix>Result
	if err = result.FromWire(body); err != nil {
		return
	}

	<if .ReturnType>success, <end>err = <$prefix>Helper.UnwrapResponse(&result)
	return
}
<end>
`

const handlerTemplate = `
// Code generated by thriftrw --generate-plugin-api
// @generated

<$module := index .Request.Modules .Service.ModuleID>
package <basename $module.ImportPath>

<$envelope := import "go.uber.org/thriftrw/internal/envelope">
<$wire     := import "go.uber.org/thriftrw/wire">

<$Handler := printf "%sHandler" .Service.Name>

// <$Handler> serves an implementation of the <.Service.Name> service.
type <$Handler> struct {
	impl <.Service.Name>

	<if .Service.ParentID>
		<$parent := getService .Request .Service.ParentID>
		<if eq $parent.ModuleID .Service.ModuleID>
			parent <$parent.Name>Handler
		<else>
			<$parentModule := index .Request.Modules $parent.ModuleID>
			parent <import $parentModule.ImportPath>.<$parent.Name>Handler
		<end>
	<end>
}

// New<$Handler> builds a new <.Service.Name> handler.
func New<$Handler>(service <.Service.Name>) <$Handler> {
	return <$Handler>{
		impl: service,
		<if .Service.ParentID>
			<$parent := getService .Request .Service.ParentID>
			<if eq $parent.ModuleID .Service.ModuleID>
				parent: New<$parent.Name>Handler(service),
			<else>
				<$parentModule := index .Request.Modules $parent.ModuleID>
				parent: <import $parentModule.ImportPath>.New<$parent.Name>Handler(service),
			<end>
		<end>
	}
}

// Handle receives and handles a request for the <.Service.Name> service.
func (h <$Handler>) Handle(name string, reqValue <$wire>.Value) (<$wire>.Value, error) {
	switch name {
		<$serviceName := .Service.Name>
		<range .Service.Functions>
			case "<.ThriftName>":
				<$prefix := printf "%s_%s_" $serviceName .Name>
				var args <$prefix>Args
				if err := args.FromWire(reqValue); err != nil {
					return <$wire>.Value{}, err
				}

				result, err := <$prefix>Helper.WrapResponse(
					h.impl.<.Name>(<range .Arguments>args.<.Name>, <end>),
				)
				if err != nil {
					return <$wire>.Value{}, err
				}

				return result.ToWire()
		<end>
		default:
			<if .Service.ParentID>
				return h.parent.Handle(name, reqValue)
			<else>
				return <$wire>.Value{}, <$envelope>.ErrUnknownMethod(name)
			<end>
	}
}
`