# KCI

KCI（Kerwin Computer Insight）是面向 Windows 的硬件信息与系统维护工具。

## 下载

- GitHub Release: https://github.com/jkx120610-commits/KCI/releases/tag/v1.0.0
- Windows x64 便携版: https://github.com/jkx120610-commits/KCI/releases/download/v1.0.0/KCI-1.0.0-win-x64.zip

## 功能

- CPU：静态参数、实时占用、封装温度、负载曲线、占用应用处置。
- GPU：显卡参数、驱动、显存、实时占用、核心温度、负载曲线、应用处置。
- WLAN：当前无线连接、SSID/BSSID、安全协议、信号、频段/信道、链路与实时上下行、测速、
  局域网设备扫描、路由器管理入口和网络优化。
- 电池：电量、健康度、设计/满充/当前容量、循环次数、充放电状态、Windows 电池报告。
- 屏幕：显示器、分辨率、刷新率、色深、DPI、工作区、纯色与灰阶测试。
- 内存与存储：RAM 模组与实时占用、SSD/HDD 健康、空间、温度和物理磁盘信息。
- KCI Help：
  - 360 全家桶卸载器：多选组件、调用卸载程序、停止服务、清理受控残留。
  - RAM 超级释放器：排除系统进程和 KCI，结束选中用户应用并生成报告。
  - SSD/HDD 超级清除：列出非系统软件、图标和占用空间，双重确认后卸载并扫描本地数据残留。
  - Windows 停止更新：可选择暂停 1 个月、半年或 1 年；暂停更新服务、更新计划任务和自动更新策略，并创建到期恢复任务。
- KCI 信息：作者、版本、产品简介、系统概览和可配置 GitHub 地址。

维护工具的操作报告保存在：

```text
%USERPROFILE%\Documents\KCI\Reports
```

## 运行要求

- Windows 10 22H2 或 Windows 11，x64。
- 便携版无需安装 .NET，发布包已包含运行时。
- 首次启动会请求管理员权限。温度、风扇、电压和存储健康等级取决于硬件、BIOS、驱动及传感器暴露情况。

## 构建

```powershell
dotnet build .\KCI.csproj -c Release -p:Platform=x64
dotnet publish .\KCI.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o .\publish
```

作者：Kerwin · GitHub：[@jkx120610-commits](https://github.com/jkx120610-commits)

## 参考实现

Windows 更新控制策略参考以下开源项目公开的实现思路，并由 KCI 重新实现可恢复的计划任务：

- W4RH4WK/Debloat-Windows-10
- Raphire/Win11Debloat
- sivel/speedtest-cli：测速流程与延迟/下载/上传测量思路
- angryip/ipscan：局域网地址发现思路
- ChrisTitusTech/winutil：Windows 网络修复命令的组织方式
