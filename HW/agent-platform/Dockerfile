# The agent platform plus the toolchain its tools shell out to.
#
#   python 3.12      the platform itself (pyproject requires >=3.12)
#   .NET SDK 10      `dotnet build` / `dotnet test` — HW.slnx needs an SDK that reads .slnx
#   ASP.NET Core 8   the runtime the net8.0 test host runs on
#   git              the VCS tools (branch detection, push)
#
# Bookworm is pinned so the libicu package name below stays valid.
FROM python:3.12-slim-bookworm

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1 \
    DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    PATH="/usr/share/dotnet:${PATH}"

RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      ca-certificates curl git libicu72 libstdc++6 tzdata zlib1g \
 && rm -rf /var/lib/apt/lists/*

RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
 && bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$DOTNET_ROOT" \
 && bash /tmp/dotnet-install.sh --channel 8.0 --runtime aspnetcore --install-dir "$DOTNET_ROOT" \
 && rm /tmp/dotnet-install.sh \
 && dotnet --list-sdks && dotnet --list-runtimes

# The repository is a bind mount owned by the host user, which git refuses by default. autocrlf=input
# keeps a Windows checkout (CRLF on disk, LF in the index) from reading as every file modified.
RUN git config --system --add safe.directory '*' \
 && git config --system core.autocrlf input

WORKDIR /app
COPY requirements.txt .
RUN pip install -r requirements.txt
COPY . .

ENTRYPOINT ["python", "cli.py"]
CMD ["status"]
