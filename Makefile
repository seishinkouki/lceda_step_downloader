# ============================================================
#  ModelDownloader — Makefile (Avalonia / .NET 10)
#
#  常用命令：
#    make                  编译 Release 版（等价于 make release）
#    make build            编译 Debug 版
#    make run              编译并运行
#    make publish-linux    发布 linux-x64 自包含版 -> publish/linux-x64/
#    make publish-aot      发布 linux-x64 NativeAOT 单文件版
#    make publish-win      发布 win-x64 NativeAOT 版（只能在 Windows 上执行）
#    make clean            dotnet clean
#    make distclean        clean + 删除 bin/obj/publish 目录
#
#  可用变量覆盖默认值：
#    make CONFIG=Debug release               指定编译配置（默认 Release）
#    make RID_LIN=linux-arm64 publish-linux  指定 Linux RID
#    make RID_WIN=win-x64 publish-win        指定 Windows RID
# ============================================================

DOTNET  ?= dotnet
CONFIG  ?= Release

SLN     := ModelDownloader.slnx
PROJ    := ModelDownloader/ModelDownloader.csproj
PROJDIR := ModelDownloader

RID_LIN ?= linux-x64
RID_WIN ?= win-x64

OUTDIR  := publish

# ---------------------------------------------------------------------------
# deb 打包：安装位置 /opt/ModelDownloader，命令行入口 /usr/bin/modeldownloader
# ---------------------------------------------------------------------------
PKG_NAME  ?= modeldownloader
VERSION   ?= 1.0.0
MAINTAINER ?= Juno <juno@localhost>
DEB_STAGING := $(OUTDIR)/deb/$(PKG_NAME)
DEB_ARCH    := $(if $(filter linux-arm64,$(RID_LIN)),arm64,$(if $(filter linux-x64,$(RID_LIN)),amd64,all))
DEB_FILE    := $(OUTDIR)/$(PKG_NAME)_$(VERSION)_$(DEB_ARCH).deb

# ---------------------------------------------------------------------------
# NativeAOT（linux-x64）的系统依赖（Debian/Ubuntu）：
#   sudo apt install clang zlib1g-dev libkrb5-dev
# ---------------------------------------------------------------------------
LIN_PUBLISH = $(DOTNET) publish $(PROJ) -c $(CONFIG) -r $(RID_LIN) --nologo

.PHONY: all check-sdk restore build release run publish publish-linux publish-aot publish-win deb clean distclean help

# 默认目标：Release 编译
all: release

# 前置检查：项目目标框架为 net10.0，需要 .NET 10 SDK
check-sdk:
	@major=$$($(DOTNET) --list-sdks 2>/dev/null | awk -F'.' '{print $$1}' | sort -n | tail -1); \
	if [ -z "$$major" ] || [ "$$major" -lt 10 ]; then \
		echo "错误: 本项目目标框架为 net10.0，需要 .NET 10 SDK（当前检测到的最高版本: $${major:-无}）"; \
		echo "下载安装: https://dotnet.microsoft.com/download/dotnet/10.0"; \
		exit 1; \
	fi

restore: check-sdk
	$(DOTNET) restore $(SLN)

build: check-sdk
	$(DOTNET) build $(PROJ) -c Debug --nologo

release: check-sdk
	$(DOTNET) build $(PROJ) -c $(CONFIG) --nologo

run: check-sdk
	$(DOTNET) run --project $(PROJ) -c $(CONFIG)

# ---------------------------------------------------------------------------
# 发布目标
# ---------------------------------------------------------------------------

publish: publish-linux

# linux-x64 自包含（非 AOT，多文件目录）
publish-linux: check-sdk
	$(LIN_PUBLISH) --self-contained -o $(OUTDIR)/$(RID_LIN)

# linux-x64 NativeAOT 单文件（需要 clang / zlib1g-dev / libkrb5-dev）
publish-aot: check-sdk
	$(LIN_PUBLISH) --self-contained -p:PublishAot=true -o $(OUTDIR)/$(RID_LIN)-aot

# win-x64 NativeAOT。
# 注意：NativeAOT 不支持从 Linux 交叉编译到 Windows，
# 本目标必须在 Windows（.NET 10 SDK + MSVC 工具链）上执行。
publish-win: check-sdk
	$(DOTNET) publish $(PROJ) -c $(CONFIG) -r $(RID_WIN) --self-contained -p:PublishAot=true --nologo -o $(OUTDIR)/$(RID_WIN)

# ---------------------------------------------------------------------------
# deb 打包（依赖 publish-linux）：
#   程序      -> /opt/ModelDownloader/
#   启动脚本  -> /usr/bin/modeldownloader
#   桌面入口  -> /usr/share/applications/modeldownloader.desktop
#   图标      -> /usr/share/icons/hicolor/256x256/apps/modeldownloader.ico
#             （来源: ModelDownloader/Assets/logo256.ico）
# 安装: sudo dpkg -i publish/modeldownloader_1.0.0_amd64.deb
# ---------------------------------------------------------------------------
deb: publish-linux
	@echo "==> 组装 deb 包..."
	rm -rf $(OUTDIR)/deb
	mkdir -p $(DEB_STAGING)/DEBIAN \
	         $(DEB_STAGING)/opt/ModelDownloader \
	         $(DEB_STAGING)/usr/bin \
	         $(DEB_STAGING)/usr/share/applications \
	         $(DEB_STAGING)/usr/share/icons/hicolor/256x256/apps
	cp -r $(OUTDIR)/$(RID_LIN)/. $(DEB_STAGING)/opt/ModelDownloader/
	printf '%s\n' \
	    '#!/bin/sh' \
	    'exec /opt/ModelDownloader/ModelDownloader "$$@"' \
	    > $(DEB_STAGING)/usr/bin/$(PKG_NAME)
	chmod 755 $(DEB_STAGING)/usr/bin/$(PKG_NAME)
	printf '%s\n' \
	    '[Desktop Entry]' \
	    'Type=Application' \
	    'Name=Model Downloader' \
	    'Name[zh_CN]=模型下载器' \
	    'Comment=立创商城 STEP 3D 模型下载器' \
	    'Exec=/usr/bin/$(PKG_NAME)' \
	    'Icon=/usr/share/icons/hicolor/256x256/apps/$(PKG_NAME).ico' \
	    'Terminal=false' \
	    'Categories=Development;Engineering;' \
	    > $(DEB_STAGING)/usr/share/applications/$(PKG_NAME).desktop
	cp $(PROJDIR)/Assets/logo256.ico $(DEB_STAGING)/usr/share/icons/hicolor/256x256/apps/$(PKG_NAME).ico
	printf '%s\n' \
	    'Package: $(PKG_NAME)' \
	    'Version: $(VERSION)' \
	    'Section: utils' \
	    'Priority: optional' \
	    'Architecture: $(DEB_ARCH)' \
	    'Maintainer: $(MAINTAINER)' \
	    'Description: 立创商城 STEP 3D 模型下载器' \
	    ' Avalonia 桌面应用，用于搜索并下载元器件 3D STEP 模型。' \
	    > $(DEB_STAGING)/DEBIAN/control
	dpkg-deb --root-owner-group --build $(DEB_STAGING) $(DEB_FILE)
	@echo "==> 生成: $(DEB_FILE)"
	@echo "==> 安装: sudo dpkg -i $(DEB_FILE)"

# ---------------------------------------------------------------------------
# 清理
# ---------------------------------------------------------------------------

clean:
	-$(DOTNET) clean $(PROJ) -c Debug    --nologo
	-$(DOTNET) clean $(PROJ) -c $(CONFIG) --nologo

distclean: clean
	rm -rf $(PROJDIR)/bin $(PROJDIR)/obj $(OUTDIR)

help:
	@echo "ModelDownloader — 可用目标:"
	@echo "  make / make release   编译 Release 版"
	@echo "  make build            编译 Debug 版"
	@echo "  make run              编译并运行"
	@echo "  make publish-linux    发布 linux-x64 自包含版"
	@echo "  make publish-aot      发布 linux-x64 NativeAOT 单文件版"
	@echo "  make publish-win      发布 win-x64 NativeAOT 版（仅限 Windows 执行）"
	@echo "  make deb              打包 deb（安装到 /opt/ModelDownloader）"
	@echo "  make clean            dotnet clean"
	@echo "  make distclean        删除 bin/obj/publish"
