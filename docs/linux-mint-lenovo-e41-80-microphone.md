# Linux Mint 22.1 下 Lenovo ZHAOYANG E41-80 麦克风配置记录

本文记录这台 Lenovo ZHAOYANG E41-80 在 Linux Mint 22.1 Xia 下，从干净系统恢复内置麦克风可用状态的步骤。当前问题和 GamePlayer 本身无关，根因是 Linux 下声卡驱动对内置数字麦克风的通道处理不正确。

## 机器与现象

- 机器：Lenovo ZHAOYANG E41-80
- 系统：Linux Mint 22.1 Xia
- 声卡：Intel HDA + Realtek ALC236
- 现象：Windows 10 下麦克风正常；Linux Mint / Ubuntu 下微信、腾讯会议、系统录音、GamePlayer ASR 都录不到人声，或只有噪声。
- 关键特征：`arecord` 能录到数据，但人声接近被抵消；录双声道时能听到大量杂音，单声道几乎没有人声。

## 快速修复步骤

先创建 ALSA 配置，让 `snd-hda-intel` 使用 `inv-dmic` 模型：

```bash
sudo tee /etc/modprobe.d/alsa-mic-fix.conf >/dev/null <<'EOF'
options snd-hda-intel model=inv-dmic
EOF
```

重启系统：

```bash
sudo reboot
```

重启后调整录音增益：

```bash
amixer -c 0 sset Digital 50%
amixer -c 0 sset Capture 80%
```

如果系统里存在 `Internal Mic Boost` 控件，可以按需要轻微增加，但不要调太高，否则容易引入电流声和爆音：

```bash
amixer -c 0 sset 'Internal Mic Boost' 1
```

## 验证

确认内核模块参数已经生效：

```bash
cat /sys/module/snd_hda_intel/parameters/model
```

输出里应能看到 `inv-dmic`。如果有多个 HDA 声卡，输出可能是逗号分隔的列表，只要对应内置声卡的位置生效即可。

录一段单声道测试音频：

```bash
arecord -D default -f S16_LE -r 16000 -c 1 -d 5 /tmp/mic-test.wav
aplay /tmp/mic-test.wav
```

如果需要对比左右声道，可以录双声道：

```bash
arecord -D default -f S16_LE -r 16000 -c 2 -d 5 /tmp/mic-stereo-test.wav
aplay /tmp/mic-stereo-test.wav
```

修复前的典型表现是录音里没有清晰人声，或只有嘈杂电流声。修复并设置增益后，说话声应能被听清，GamePlayer ASR 也应恢复正常。

## GamePlayer 相关说明

GamePlayer 不需要额外启动参数来启用 Linux 麦克风自动探测。只要在 GameEditor 的项目设置里启用了麦克风自动探测，GamePlayer 加载项目时就会自动探测。

当前 GamePlayer 的 PortAudio 自动探测在子进程中执行。这样即使某些 Linux ALSA / PortAudio 设备探测触发 native 层崩溃，也只会影响探测子进程，不会让主 GamePlayer 闪退。探测失败时，本次运行会禁用 ASR / Realtime Voice 的麦克风输入功能，并在日志里输出原因。

## 回滚

如果这台机器之后换了内核、声卡驱动或外接麦克风方案，`inv-dmic` 不再适用，可以删除配置并重启：

```bash
sudo rm /etc/modprobe.d/alsa-mic-fix.conf
sudo reboot
```

回滚后再用 `arecord` 重新测试。

## 排查命令

查看声卡和录音设备：

```bash
arecord -l
arecord -L
```

查看 ALSA 控件：

```bash
amixer -c 0
```

查看 PipeWire / PulseAudio 默认输入源：

```bash
pactl info
pactl list short sources
```

如果 `arecord` 直接录不到人声，微信、腾讯会议和 GamePlayer 通常也不会正常；应先按本文修复系统麦克风，再排查应用层配置。
