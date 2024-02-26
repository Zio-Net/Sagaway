FROM mcr.microsoft.com/dotnet/sdk:6.0

# Install the simulator tool
RUN dotnet tool install -g Microsoft.Azure.SignalR.Emulator --version 1.1.0
ENV PATH="$PATH:/root/.dotnet/tools"

# create a current working directory
RUN mkdir /workspace
WORKDIR /workspace

COPY /SignalREmulator/settings.json /workspace/
# init default settings.json
RUN asrs-emulator upstream init

# configurations
EXPOSE ${ASRS_EMULATOR_PORT}

# start the emulator
ENTRYPOINT asrs-emulator start -i 0.0.0.0 --port $ASRS_EMULATOR_PORT