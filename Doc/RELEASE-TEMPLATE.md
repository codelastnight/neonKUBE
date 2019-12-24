This is a full Neon release intended for general consumption.  Note that some packages may not be at release quality yet.

<table>
  <tr>
    <td>Documentation</td>
    <td><a href="https://doc.neonkube.com">https://doc.neonkube.com</a></td>
  </tr>
  <tr>
    <td>GitHub Repository:</td>
    <td><a href="https://github.com/nforgeio/neonKUBE">https://github.com/nforgeio/neonKUBE</a></td>
  </tr>
  <tr>
    <td>Cadence samples:</td>
    <td><a href="https://github.com/nforgeio/cadence-samples">https://github.com/nforgeio/cadence-samples</a></td>
  </tr></table>

## Highlights:

**TODO:** Write something here!

### <img src="https://doc.neonkube.com/media/release.png"/> Neon.Cadence

This package provides a .NET client for the [Uber Cadence](https://cadenceworkflow.io/docs/) workflow platform.  This is still a bit of a work in progress.  That being said, most functionality is implemented and we're using this in production for a day-job project.  You can get started [here](https://doc.neonkube.com/Neon.Cadence-Overview.htm).

**Changes:** 
* `CadenceSettings` adds a constructor where you can specify the server URIs

### <img src="https://doc.neonkube.com/media/release.png"/> Neon.Common 

This package includes a few dozen useful types used by other Neon components.  This is a relatively small assembly and is suitable for including in both app and server side applications, including Xamarin iOS and Android applications (we haven't tested UWP).

**Changes:** No significant changes for this release.

### <img src="https://doc.neonkube.com/media/release.png"/> Neon.Couchbase

This package extends the standard [CouchbaseNetClient](https://www.nuget.org/packages/CouchbaseNetClient) package, adding *safe* methods that return the operation result directly, handling transient errors transparently and throwing exceptions for hard errors.  This also includes some configuration related extensions.  You can get started [here](https://doc.neonkube.com/Neon.Couchbase-Overview.htm).

**Changes:** 
* `CouchbaseSettings` adds a constructor where you can specify the server URIs

### <img src="https://doc.neonkube.com/media/release.png"/> Neon.Cryptography

This package includes some helpers making MD5 and SHA-* hashing a bit easier and as well as a class intended for encrypting data at rest.  You can get started [here](https://doc.neonkube.com/Neon.Cryptography-Overview.htm).

**Changes:** No significant changes for this release.

### <img src="https://doc.neonkube.com/media/release.png"/> Neon.Kube.Service

This package includes types that implement much of the boilerplate code required for Docker container or Kubernetes applications.  This includes integrated logging and support for termination signals on Linux and OS/X as well as abstractions you can use to unit test your services.  You can get started [here](https://doc.neonkube.com/Neon.Kube.Service-Overview.htm)

**Changes:** No significant changes for this release.

### <img src="https://doc.neonkube.com/media/preview.png"/> Neon.ModelGen

This package is designed to convert compiled .NET interface definitions into data model classes that can support round-trip data transmission without loss in many common scenarios providing a way to help future-proof your applications.  This also can support generating the boilerplate code implementing  `INotifyPropertyChanged` for UX applications well as generating REST service clients.

Most developers won't need to reference this library since the functionally is exposed by this **neon-cli** command:
```
neon generate models ...
```
This is still a work in progress.  The `INotifyPropertyChanged` and REST client generation support works now but the round-trip functionally doesn't work yet.

**Changes:** No significant changes for this release.

### <img src="https://doc.neonkube.com/media/release.png"/> Neon.NATS

This package extends the [NATS Scalabable Messaging Platform](https://nats.io/), adding support for round-trip data models generated by **Neon.ModelGen** as well as some other handy utilities.  These extensions are added to the [NATS.Client](https://doc.neonkube.com/N_NATS_Client.htm) and [STAN.Client](https://doc.neonkube.com/N_STAN_Client.htm) namespaces.

**Changes:** No significant changes for this release.

### <img src="https://doc.neonkube.com/media/release.png"/> Neon.SSH.NET

This is a semi-clone of the [https://github.com/sshnet/SSH.NET](>https://github.com/sshnet/SSH.NET) project which hasn't been supported for a while.  This fixes this issue [#515: Bugfix for "scp: error: unexpected filename](https://github.com/sshnet/SSH.NET/pull/515) that was preventing us from connecting to modern Linux distributions.

**Changes:** No significant changes for this release.

### <img src="https://doc.neonkube.com/media/release.png"/> Neon.Web 

Handy ASP.NET related extensions and utilities including new base controller classes that provide integrated logging.

**Changes:** No significant changes for this release.

### <img src="https://doc.neonkube.com/media/release.png"/> Neon.Xunit

This package includes generally useful test fixtures for ASP.NET, Containers, Docker, NATS as well as a fixture that can be used to compose fixtures including your custom fixtures.

**Changes:** No significant changes for this release.

### <img src="https://doc.neonkube.com/media/release.png"/> Neon.Xunit.Cadence

This package includes a fixture for running Cadence locally as a Docker container during for unit tests.

**Changes:** No significant changes for this release.

### <img src="https://doc.neonkube.com/media/release.png"/> Neon.Xunit.Couchbase

This package includes a fixture for running Couchbase locally as a Docker container during for unit tests.

**Changes:** No significant changes for this release.

### <img src="https://doc.neonkube.com/media/alpha.png"/> Neon.Xunit.Kube

This package is currently used internally for neonKUBE related unit testing.

**Changes:** No significant changes for this release.

### <img src="https://doc.neonkube.com/media/alpha.png"/> neonKUBE Desktop

We're working on yet another Kubernetes distribution but this is still very much a work in progress.  Although we include the install binaries for this, we recommend that you folks avoid these until we're further along.

<table>
  <tr>
    <td width="85px" align="center"><img src="https://doc.neonkube.com/media/alpha.png"/></td>
    <td><b>neon-osx:</b> is the OS/X build of the <b>neon-cli</b> command line tool.  We're currently using this internally in a day-job project for generating data models using <b>ModelGen</b>.  This will eventually be included in a proper OS/X installer.</td>
  </tr>
  <tr>
    <td><img src="https://doc.neonkube.com/media/alpha.png"/></td>
    <td><b>neonKUBE-setup-#.#.#.exe:</b> Installs the neonKUBE Desktop as well as the <b>neon-cli</b> command line tool.</td>
  </tr>
</table>

For Windows, you simply need to download and run **neonKUBE-setup-#.#.#.exe** to install or upgrade **neonKUBE Desktop** and the **neon-cli** command line tool. 

We don't have an OS/X version of the desktop yet, but you can manually install **neon-cli** via:
1. Download the **neon-osx** file below.  This will appear in Safari downloads as **neon-osx.dms**.
2. Manually copy **neon-osx.dms** below to your `/usr/local/bin` directory (we don't have a **.dmg** file yet).
3. Open a terminal window and run these commands:
    ```
    sudo bash
    cd /usr/local/bin
    rm neon
    mv neon-osx.dms neon
    chmod 777 neon
    spctl --master-disable
    ```

Neon related production images are hosted on DockerHub: [here](https://hub.docker.com/orgs/nkubeio/repositories)

**Changes:** No significant changes for this release.

Neon components are released using versions compatible with [Semantic Versioning 2.0](https://semver.org/).  All packages and binaries are unit tested together before being published and you should upgrade all Neon nuget packages together so that all have the same version number.  Note that some packages may have pre-release identifier, indicating that component is still a work in progress or that a package is only for use by other Neon components.

<table>
  <tr>
    <td width="85px" align="center"><img src="https://doc.neonkube.com/media/release.png"/></td>
    <td>indicates that the release is expected suitable for production use. Released binary versions follow the semantic version 2.0 specification and don't include a pre-release identifier.</td>
  </tr>
  <tr>
    <td><img src="https://doc.neonkube.com/media/preview.png"/></td>
    <td>indicates that the released binary still has some work in progress but is relatively stable and also that we expect that we we'll try to avoid making significant breaking changes to the API surface area. This may be suitable for production but you should take care.</td>
  </tr>
  <tr>
    <td><img src="https://doc.neonkube.com/media/alpha.png"/></td>
    <td>indicates that the released binary is not ready for general production use. There are likely to be serious bugs and implementation gaps and it is also very likely that the API may still see very significant changes. We do early alpha releases to give interested parties a chance to review what we're doing and also so that we and close partners can give these a spin in test and sometimes production.</td>
  </tr>
  <tr>
    <td><img src="https://doc.neonkube.com/media/internal.png"/></td>
    <td>indicates that the released binary is not intended for general consumption. These are typically referenced by other neonKUBE libaries and tools. </td>
  </tr>
</table>

### Binary SHA512 signatures:

**neonKUBE-setup-1.0.0.exe:**
`FILL THIS IN`

**neon.chm:**
`FILL THIS IN`

**neon-osx:**
`FILL THIS IN`