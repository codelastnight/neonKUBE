﻿# neonKUBE Developer Setup

This page describes how to get started with neonKUBE development.

## Workstation Requirements

* Windows 10 Professional (64-bit) with at least 16GB RAM
* Virtualization capable workstation
* Visual Studio 2019 Edition (or better)
* Visual Studio Code

Note that the build environment currently assumes that only one Windows user will be acting as a developer on any given workstation.  Developers cannot share a machine and Neon only builds on Windows at this time.

## Workstation Configuration

Follow the steps below to configure a development or test workstation:

1. Make sure that Windows is **fully updated**.

2. We highly recommend that you configure Windows to display hidden files:

    * Press the **Windows key** and run **File Explorer**
    * Click the **View** tab at the top.
    * Click the **Options** icon on the right and select **Change folder and search options**.
    * Click the **View** tab in the popup dialog.
    * Select the **Show hidden files, folders, and drives** radio button.
    * Uncheck the **Hide extensions for known types** check box.

3. Some versions of Skype listen for inbound connections on ports **80** and **443**.  This will interfere with services we'll want to test locally.  You need to disable this:

    * In Skype, select the **Tools/Options** menu.
    * Select the **Advanced/Connection** tab on the left.
    * **Uncheck**: Use **port 80 and 443** for additional incoming connections.

      ![Skype Connections](Images/DEVELOPER/SkypeConnections.png?raw=true)
    * **Restart Skype**

4. Ensure that Hyper-V is installed and enabled:

    a. Run the following command in a **cmd** window to verify that your workstation is capable of virtualization and that it's enabled. You're looking for output like the image below:
      ```
      systeminfo
      ```
      ![Virtualization Info](Images/DEVELOPER/virtualization.png?raw=true)

    or a message saying that: **A hypervisor has been detected.**

    b. Press the Windows key and enter: **windows features** and press ENTER.

    c. Ensure that the check boxes highlighted in red below are checked:

    ![Hyper-V Features](Images/DEVELOPER/hyper-v.png?raw=true) 

    d. Reboot your machine as required.

5. Install the latest **32-bit** production release of PowerShell Core from [here](https://github.com/PowerShell/PowerShell/releases) (`PowerShell-#.#.#-win.x86.msi`)

6. Enable PowerShell script execution via (in a CMD window as administrator):
    ```
    powershell Set-ExecutionPolicy -ExecutionPolicy Unrestricted -Scope CurrentUser
    ```

  `powershell Set-ExecutionPolicy -ExecutionPolicy Unrestricted -Scope CurrentUser`

7. Install **Visual Studio 2019 Community 16.3+** from [here](https://visualstudio.microsoft.com/thank-you-downloading-visual-studio/?sku=Community&rel=16)

  * Select **all workloads** on the first panel
  * Click **Individual components**, type *Git* in the search box and select **Git for Windows** and **GitHub extension for Visual Studio**
  * Click **Install** (and take a coffee break)
  * Install **.NET Core SDK 3.1.100 (Windows .NET Core Installer x64)** from [here](https://dotnet.microsoft.com/download/visual-studio-sdks)
  * Apply any pending **Visual Studio updates**
  * **Close** Visual Studio and install any updates
  
8. Create a **shortcut** for Visual Studio and configure it to run as **administrator**.  To build and run neonKUBE applications and services, **Visual Studio must be running with elevated privileges**.

9. Install **.NET Framework 4.8 Developer Pack** from [here](https://dotnet.microsoft.com/download/thank-you/net48-developer-pack)

10. Install **Visual Studio Code** from [here](https://code.visualstudio.com/download)

11. Configure the GOLANG development environment:

    * Install **go1.13.windows-amd64.msi** or later for Windows from: [here](https://golang.org/dl/)

12. Install **Docker for Windows (Stable)** from [here](http://hub.docker.com)

    * You'll need to create a DockerHub account if you don't already have one.
    * **Right-click** the Docker icon in the system tray and select **Settings...**

      ![System Tray](Images/DEVELOPER/DockerSysTray.png?raw=true)

    * Select the **Shared Drives** tab click the check box to **share** the drive where you'll clone the project source code (typically drive C:)
    * Click **Apply** (You may need to enter your workstation **credentials**).
    * Select the **Advanced** tab and enable **Manual DNS Configuration** and set the DNS to **8.8.8.8**.
    * Click **Apply**
    * Select the **Resource** tab on the left set **CPUs=4** and **Memory=4GB**.
    * Click **Apply**

13. **Clone** the [https://github.com/nforgeio/neonKUBE](https://github.com/nforgeio/neonKUBE) repository to your workstation:

    * Create an individual GitHub account [here](https://github.com/join?source=header-home) if you don't already have one
    * Have one of the neonKUBE repository administrators **grant you access** to the repository
    * Go to [GitHub](http://github.com) and log into your account
    * Go to the neonKUBE [repository](https://github.com/nforgeio/neonKUBE).
    * Click the *green* **Clone or download** button and select **Open in Visual Studio**
    * A *Launch Application* dialog will appear.  Select **Microsoft Visual Studio Protocol Handler Selector** and click **Open Link**
    * Choose or enter the directory where the repository will be cloned.  This defaults to a user-specific folder.  I typically change this to a global folder to keep the file paths short.
    
      ![Video Studio Clone](Images/DEVELOPER/VisualStudioClone.png?raw=true)
    * Click **Clone**

14. **Close** any running instances of **Visual Studio**

15. Install the latest release of **neonKUBE** from [here](https://github.com/nforgeio/neonKUBE/releases)

16. Install **7-Zip (32-bit)** (using the Windows *.msi* installer) from [here](http://www.7-zip.org/download.html)

17. Install **Cygwin - setup-x86-64.exe** (all packages and default path) from: [here](https://www.cygwin.com/setup-x86_64.exe)

18. Many server components are deployed to Linux, so you’ll need terminal and file management programs.  We’re currently standardizing on **PuTTY** for the terminal and **WinSCP** for file transfer. install both programs to their default directories:

    * Install **WinSCP** from [here](http://winscp.net/eng/download.php) (I typically use the "Explorer" interface)
    * Install **PuTTY** from [here](https://www.chiark.greenend.org.uk/~sgtatham/putty/latest.html)
    * *Optional*: The default PuTTY color scheme sucks (dark blue on a black background doesn’t work for me).  You can update the default scheme to Zenburn Light by **right-clicking** on the `$\External\zenburn-ligh-putty.reg` in **Windows Explorer** and selecting **Merge**
    * WinSCP: Enable **hidden files**.  Start **WinSCP**, select **Tools/Preferences...**, and then click **Panels** on the left and check **Show hidden files**:
    
      ![WinSCP Hidden Files](Images/DEVELOPER/WinSCPHiddenFiles.png?raw=true)

19. Configure the build **environment variables**:

    * Open **File Explorer**
    * Navigate to the directory holding the cloned repository
    * **Right-click** on **buildenv.cmd** and then **Run as adminstrator**
    * Close the CMD window when the script is finished
  
20. Install the latest release build of neonKUBE from [here](https://github.com/nforgeio/neonKUBE/releases)

21. Restart Visual Studio (to pick up the environment changes).

22. Confirm that the solution builds:

    * Run **Visual Studio** as **administrator**
    * Open **$/neonKUBE.sln** (where **$** is the repo root directory)
    * Select **Build/Rebuild** Solution

23. Install **InnoSetup** from the **$/External** directory using default settings.  This is required to build the Windows neonKUBE installer.

24. *Optional*: Install **OpenVPN**

    * Download the Windows Installer from [here](https://openvpn.net/index.php/open-source/downloads.html)
    * Run this command as administrator in a CMD window to install a second TAP interface:
      ```
      "%PROGRAMFILES%\Tap-Windows\bin\addtap.bat"
      ```
    * Obtain your WowRacks VPN credentials from another developer who has ADMIN access.

25. *Optional*: Install **Notepad++** from [here](https://notepad-plus-plus.org/download)

26. *Optional*: Install **Postman** REST API tool from [here](https://www.getpostman.com/postman)

27. *Optional*: Install **Cmdr/Mini** command shell:

  * **IMPORTANT: Don't install the Full version** to avoid installing Linux command line tools that might conflict with the Cygwin tools installed earlier.
  * Download the ZIP archive from: [here](http://cmder.net/)
  * Unzip it into a new folder and then ensure that this folder is in your **PATH**.
  * Create a desktop shortcut if you wish and configure it to run as administrator.
  * Run Cmdr and configure settings as desired.
  * Consider removing the alias definitions in `$\config\user-aliases.cmd` file so that commands like `ls` will work properly.  I deleted all lines beneath the first `@echo off`.

28. *Optional*: Install the latest version of **XCP-ng Center** from [here](https://github.com/xcp-ng/xenadmin/releases) if you'll need to manage Virtual Machines hosted on XCP-ng.

29. *Optional*: Developers who will be publishing releases will need to:

    * **Download:** the latest recommended **nuget.exe** from [here](https://www.nuget.org/downloads) and put this somewhere in your `PATH`
    * Obtain a nuget API key from a senior developer and install the key on their workstation via:
      ```
      nuget SetApiKey APIKEY
      ```
    * **Close:** all Visual Studio instances.
    * **Install:** the HTML Help Compiler by running `$/External/htmlhelp.exe` with the default options.  You can ignore any message about a newer version already being installed.
    * **Unzip:** `$/External/SHFBInstaller_v2019.9.15.0.zip` to a temporary folder and run `SandcastleInstaller.exe`, then:
      * Click **Next** until you get to the **Sandcastle Help File Builder and Tools** page.
      * Click **Next** and then **Install SHFB**
      * Go through the wizard, accepting the licence and use the default options.
      * Click **Finish** to close the SHFB installer.
      * Click **Next** in the guided installation and then **Install Package** to install the Visual Studio package.
      * Click **Next** and click **Install Schemas**
      * **Optional:** Install Snippets (I don't don't install these myself)
      * Click **Next** and **Close**.  Don't install the Visual Studio Spell Checker.
    * Clone the **nforgeio/nforgeio.github.io** repository.  This hosts the generated Neon documentation website.  The cloned folder must be named **nforgeio.github.io** and be located in the same parent directory as your main neonKUBE repo:
    ```
    cd "%NF_ROOT%\.."
    mkdir nforgeio.github.io
    git clone https://github.com/nforgeio/nforgeio.github.io.git
    ```
    * You'll also need to clone the **nforgeio/cadence-samples** repo.  This hosts the Cadence samples which are referenced by the code documention.  The cloned folder must be named **cadence-samples** and be located in the same parent directory as your main neonKUBE repo: 
    ```
    cd "%NF_ROOT%\.."
    mkdir cadence-samples
    git clone https://github.com/nforgeio/cadence-samples.git
    ```

30. *Optional*: Create the **EDITOR** environment variable and point it to `C:\Program Files\Notepad++\notepad++.exe` or your favorite text editor executable.
