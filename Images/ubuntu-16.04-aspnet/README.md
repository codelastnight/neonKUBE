Ubuntu 16.04 image with the .NET Core (includes ASP.NET)

# Image Tags

These images are tagged with the Microsoft .NET Core runtime version plus the image build date.  The most recent build will be tagged as **latest**.

From time-to-time you may see images tagged like `:BRANCH-*` where **BRANCH** identifies the Git source branch where the image was built from.  These images are used for internal development purposes only and **should not be used production** as they may not actually work and may also be removed or updated at any time.

# Description

Note that any images that extend this one should launch the [tini](https://github.com/krallin/tini) init manager as the first process within the container so that Linux signals will be forwarded to child processes and so zombie processes will be reaped.  You'll need to specify a Docker entrypoint like:

&nbsp;&nbsp;&nbsp;&nbsp;`ENTRYPOINT ["tini", "-g", "--", "/docker-entrypoint.sh"]`

# Additional Packages

This image extends the latest [nhive/ubuntu-16.04](https://hub.docker.com/r/nhive/ubuntu-16.04/) by installing the .NET Core runtime (including ASP.NET).  The image tag identifies the .NET Core version installed.