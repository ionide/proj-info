#!/usr/bin/env bash
set -euxo pipefail

source /etc/os-release

# required by apt-key
sudo apt install -y gnupg2
# required by apt-update when pulling from mono-project.com
sudo apt install -y ca-certificates

# taken from http://www.mono-project.com/download/stable/#download-lin
sudo apt-key adv --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys 3FA7E0328081BFF6A14DA29AA6A19B38D3D831EF
sudo echo "deb https://download.mono-project.com/repo/ubuntu stable-$UBUNTU_CODENAME main" | sudo tee /etc/apt/sources.list.d/mono-official-stable.list
sudo apt update
sudo apt install -y fsharp
