# syntax=docker/dockerfile:1

# Bocchi HomeServer 镜像：Admin 前端资源 → .NET 发布 → 精简运行时。
# 前两个阶段固定跑在构建机架构上，arm64 镜像通过 dotnet publish -a 交叉发布，不依赖 QEMU 编译。

FROM --platform=$BUILDPLATFORM node:22-bookworm-slim AS admin-assets
WORKDIR /src/Src/HomeServer/Bocchi.HomeServer
COPY Src/HomeServer/Bocchi.HomeServer/package.json Src/HomeServer/Bocchi.HomeServer/package-lock.json ./
RUN npm ci
# Tailwind 需要扫描 Razor 组件里的 class，所以整个项目目录都要在这里
COPY Src/HomeServer/Bocchi.HomeServer/ ./
RUN npm run build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
# 发布流程传入 tag（如 v1.2.0），本地构建不传时沿用项目默认版本号
ARG BOCCHI_VERSION=
WORKDIR /src
COPY .editorconfig global.json Directory.Build.props Directory.Packages.props Bocchi.slnx ./
COPY Src/ Src/
COPY Themes/ Themes/
COPY --from=admin-assets /src/Src/HomeServer/Bocchi.HomeServer/wwwroot/ Src/HomeServer/Bocchi.HomeServer/wwwroot/
RUN --mount=type=cache,id=bocchi-nuget,target=/root/.nuget/packages \
    dotnet publish Src/HomeServer/Bocchi.HomeServer/Bocchi.HomeServer.csproj \
        -c Release -a "$TARGETARCH" --self-contained false \
        -p:BocchiSkipAdminAssetsBuild=true ${BOCCHI_VERSION:+-p:Version=${BOCCHI_VERSION#v}} -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
# 预建 /data 并交给内置的非 root 用户 app，命名卷首次挂载时会继承这个属主
RUN mkdir -p /data && chown "$APP_UID" /data
USER $APP_UID
# 监听地址统一由 Kestrel 配置决定；清空基础镜像的 HTTP_PORTS，避免启动时的覆盖警告
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS= \
    Bocchi__DataRoot=/data \
    Kestrel__Endpoints__Http__Url=http://0.0.0.0:8080
EXPOSE 8080
VOLUME ["/data"]
# 运行时镜像没有 curl，用 bash 的 /dev/tcp 请求 /healthz
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /healthz HTTP/1.0\\r\\nHost: localhost\\r\\n\\r\\n' >&3 && head -n 1 <&3 | grep -q ' 200 '"]
# 额外参数直接传给程序，例如：docker compose run --rm bocchi backup
ENTRYPOINT ["dotnet", "Bocchi.HomeServer.dll"]
