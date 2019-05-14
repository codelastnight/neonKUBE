package server

import (
	"context"
	"net/http"
	"os"
	"time"

	"github.com/go-chi/chi"
	"go.uber.org/zap"
)

// Instance is a server instance that contains
// a reference to an http.Server in memory,
// a reference to an existing zap.Logger,
// and a reference to an existing chi.Mux
type Instance struct {
	httpServer      *http.Server
	Logger          *zap.Logger
	Router          *chi.Mux
	ShutdownChannel chan bool
}

// NewInstance initializes a new instance of the server Instance
//
// param addr string -> the desired address for the server to
// listen and serve on
//
// returns *Instance -> Pointer to an Instance object
func NewInstance(addr string) *Instance {

	// Router defines new chi router to set up the routes
	var router = chi.NewRouter()

	// do any server instance setup here
	s := &Instance{
		Router:          router,
		httpServer:      &http.Server{Addr: addr, Handler: router},
		ShutdownChannel: make(chan bool),
	}

	return s
}

// Start sets a zap.Logger for a server Instance
// ListenAndServers on the configured server address,
// and provides functionality for a clean shutdown if the server
// shuts down unexpectedly
func (s *Instance) Start() {

	// set the logger to the global
	// zap.Logger
	s.Logger = zap.L()
	s.Logger.Info("Server Details",
		zap.String("Address", s.httpServer.Addr),
		zap.Int("ProccessId", os.Getpid()),
	)

	// listen and serve (for your country)
	go func() {
		if err := s.httpServer.ListenAndServe(); err != nil && err != http.ErrServerClosed {
			s.Logger.Fatal("http server stopped unexpenctedly", zap.Error(err))
		}
	}()

	// wait for the shutdown signal from a terminate request
	shutdown := <-s.ShutdownChannel
	if shutdown {
		// create the context and the cancelFunc to shut down the server instance
		ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
		defer cancel()

		// try and do a graceful shutdown if possible from context
		if err := s.httpServer.Shutdown(ctx); err != nil {
			s.Logger.Fatal("could not gracefully shut server down", zap.Error(err))
		}
		s.Logger.Info("server gracefully shutting down")
	}
}
