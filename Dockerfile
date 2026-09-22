# Parallel Factory MES のコンテナイメージ（API＋Blazor WebAssembly クライアントを1つに同梱）
#
#   docker build -t parallelfactorymes .
#   docker run -d -p 8080:8080 -v mesapp-data:/data parallelfactorymes
#
# amd64 / arm64 の両方を作るときは buildx を使う（ビルド段はホストのアーキテクチャで動かし、
# 対象アーキテクチャ向けにクロス発行するので QEMU でのコンパイルは発生しない）:
#   docker buildx build --platform linux/amd64,linux/arm64 -t parallelfactorymes .

# ---- ビルド ----------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
ARG VERSION=1.0.0
WORKDIR /src

# 依存の復元だけを先に行い、ソース変更時もパッケージ取得の層を再利用する
COPY Directory.Build.props ./
COPY src/MesApp.Api/MesApp.Api.csproj src/MesApp.Api/
COPY src/MesApp.Client.Web/MesApp.Client.Web.csproj src/MesApp.Client.Web/
COPY src/MesApp.Core/MesApp.Core.csproj src/MesApp.Core/
COPY src/MesApp.Infrastructure/MesApp.Infrastructure.csproj src/MesApp.Infrastructure/
COPY src/MesApp.Migrations.PostgreSql/MesApp.Migrations.PostgreSql.csproj src/MesApp.Migrations.PostgreSql/
COPY src/MesApp.Migrations.SqlServer/MesApp.Migrations.SqlServer.csproj src/MesApp.Migrations.SqlServer/
RUN dotnet restore src/MesApp.Api/MesApp.Api.csproj -a $TARGETARCH

COPY src/ src/
RUN dotnet publish src/MesApp.Api/MesApp.Api.csproj -c Release -a $TARGETARCH --no-restore \
        -o /app -p:Version=$VERSION -p:DebugType=None -p:DebugSymbols=false

# ---- 実行 ------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
ARG VERSION=1.0.0

LABEL org.opencontainers.image.title="Parallel Factory MES" \
      org.opencontainers.image.description="製造実行システム（MES）。API と Blazor WebAssembly クライアントを同梱" \
      org.opencontainers.image.source="https://github.com/msmsrep/ParallelFactoryMES" \
      org.opencontainers.image.licenses="AGPL-3.0-only" \
      org.opencontainers.image.version="$VERSION"

# DB（SQLite）と JWT 署名鍵は /data に置く。コンテナを作り直しても残るようボリュームにする
# TZ は業務日付の境界（BusinessDay:BoundaryHour）と CSV のファイル名の日付に効く。日本以外では上書きする
ENV MESAPP_DATA_DIR=/data \
    TZ=Asia/Tokyo \
    ASPNETCORE_HTTP_PORTS=8080
RUN mkdir -p /data && chown $APP_UID /data
VOLUME /data

WORKDIR /app
COPY --from=build /app ./
COPY LICENSE ./LICENSE.txt

USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "MesApp.Api.dll"]
