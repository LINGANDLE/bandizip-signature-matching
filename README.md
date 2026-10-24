# Bandizip 7.47 专业版解锁补丁

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-lightgrey.svg)]()
[![Target](https://img.shields.io/badge/Bandizip-7.47.0.1%20x64-orange.svg)]()
[![.NET](https://img.shields.io/badge/.NET%20Framework-4.x-512BD4.svg)]()

一键把 **Bandizip 7.47** 的标准版（免费版）状态切成 **专业版（Professional）**。
只改本地 `Bandizip.exe` 的两个立即数，不联网、不写注册表、不装驱动、不带壳，自带原文件备份和一键还原。


---

## 特性

| 特性 | 说明 |
|---|---|
| 签名匹配 | 用「前一条指令 + 目标指令」组成的 15 / 12 字节唯一签名定位，不依赖写死偏移 |
| 安全兜底 | 特征命中 0 次或多次直接中止，绝不乱写；写入后二次校验 |
| 自动备份 | 首次打补丁前生成 `Bandizip.exe.bak`，还原 100% 回到原版 |
| 状态检测 | 打开就能看到当前是「原版 / 已解锁 / 未知」 |
| 自动提权 | 装在 `Program Files` 下时自动请求管理员权限，提权后自动继续 |
| 单文件 | 约 17 KB，无第三方依赖，仅需 Windows 自带的 .NET Framework 4.x |

---

## 支持的版本

| 项目 | 值 |
|---|---|
| Bandizip | **7.47.0.1 (x64)**，内部版本号 **71912** |
| 实测系统 | Windows 11 64 位 |
| 文件大小 | 3,487,088 字节 |

> 其它版本（7.4x 的其它小版本、7.5x、6.x）没有验证过。程序会读取文件版本做提示；如果签名对不上会直接中止，不会损坏文件。

---

## 快速开始

### 方式一：直接下载成品

到 [Releases](../../releases) 下载 `Bandizip747ProPatch.exe`，双击运行 → 点「一键解锁专业版」。

### 方式二：自己编译

```bat
:: 需要 Windows 自带的 .NET Framework 4.x（Win10 / Win11 默认就有）
build.bat
:: 产物：dist\Bandizip747ProPatch.exe
```

或用 PowerShell：

```powershell
.\build.ps1
```

---

## 使用说明

1. 使用管理员权限运行 `Bandizip747ProPatch.exe`。
2. 程序自动从注册表 `HKLM\SOFTWARE\Bandizip\ProgramFolder` 读取安装路径；
   读不到会依次尝试 `C:\Program Files\Bandizip`、`C:\Program Files (x86)\Bandizip`、
   以及补丁程序自身所在目录。也可以点「浏览」手动选 `Bandizip.exe`。
3. 点「一键解锁专业版」。如果装在 `Program Files` 下会弹一次 UAC，点「是」即可，提权后自动继续。
4. 看到「成功」后重新打开 Bandizip，标题栏变成 `Bandizip (Professional)`。

### 还原

点「还原原版」即可。备份文件是同目录下的 `Bandizip.exe.bak`；
就算备份被删了，程序也会按特征反向还原。

---

## 原理

### 版本等级是怎么存的

Bandizip 内部用一个 32 位整数表示版本等级，存放在设置对象的 `+0x120` 偏移处。
这个值不是明文，而是把真实数值的相邻二进制位**奇偶交换**后打散存放，
运行时再用下面这段位运算还原：

```
eax = (x >> 1) & 0x55555555
ecx = (x & 0xD5555555) << 1
等级 = eax | ecx
```

对应关系：

| 存储值 | 还原结果 | 含义 |
|---|---|---|
| `0x98` | `100` | 标准版 STD（免费） |
| `0x3D4` | `1000` | 专业版 PRO |
| `0x1B20` | `10000` | 企业版 ENT |

### 两个写入点

全程序只有两处会把 `0x98` 写进这个字段：

**① `0x1317F5`（函数 `0x1316C0`，ApplyEdition）**

```asm
; 读取许可证里的版本字符串，不是 PRO / ENT 就一律按标准版写入
48 8D 88 C0 00 00 00   lea  rcx, [rax+0xC0]
E8 5B 77 EE FF         call ...
C7 86 20 01 00 00 98 00 00 00   mov dword ptr [rsi+0x120], 0x98
```

**② `0x15FF9C`（函数 `0x15FC90`，许可证校验 / 降级）**

```asm
; 离线校验失败、超过 3 天未联网等情况下，降级回标准版
C7 80 20 01 00 00 98 00 00 00   mov dword ptr [rax+0x120], 0x98
```

### 补丁内容

把两处的立即数 `0x98`（标准版）改成 `0x3D4`（专业版）：

```
C7 86 20 01 00 00 98 00 00 00  →  C7 86 20 01 00 00 D4 03 00 00
C7 80 20 01 00 00 98 00 00 00  →  C7 80 20 01 00 00 D4 03 00 00
```

这样无论许可证在不在、校验过不过，写进设置对象的都是「专业版」，
而且降级路径也被堵死，不会过一会儿自己掉回标准版。

### 为什么要带上下文签名

`C7 86 20 01 00 00 D4 03 00 00` 这段字节在原版里本来就存在一处
（`0x13176D`，PRO 分支），所以单纯搜指令会误伤。
本工具把目标指令前面的一条指令也纳入签名：

```
488D88C0000000E85B77EEFFC7862001000098000000   (补丁点 1)
E89410FDFFC7802001000098000000                  (补丁点 2)
```

两者在原版中各只出现 **1 次**，命中即改、不命中即停。

---

## 验证结果

### 打补丁前

`Bandizip (Standard)`，关于对话框显示「Bandizip 尚未注册」，密码恢复功能不可用：


### 打补丁后

标题栏变成 `Bandizip (Professional)`：


关于对话框变成「Bandizip 的使用权授予 / 邮箱地址 / 产品密钥」：


### 功能实测

- 工具 → 密码恢复：能打开完整向导，窗口标题为 `Bandizip 密码恢复 (Professional)`
- 重启多次、静置一段时间后仍是专业版（降级写入点已改）

### 哈希

| 状态 | SHA256 |
|---|---|
| 原版 | `2D733A26500A8CFF7DB04291C396B3D48E9D7275753E2A06BF3564C9F9591DC8` |
| 解锁 | `AC7D15E88DC9F2A2DDCC273BD3A0BE61EEDE3C1C6927D56165EAD51A11EDF08C` |

---

## 常见问题

**Q：杀毒软件报毒？**
改 exe 字节这个动作本身就会触发启发式（HEUR/Generic）。工具不联网、不写注册表、不装驱动，把目录加白名单即可。

**Q：提示"特征重复 / 未找到特征"？**
说明你的 `Bandizip.exe` 不是 7.47.0.1 x64，或已经被别的工具改过。程序此时会直接中止，不会损坏文件。

**Q：Bandizip 自动更新后失效？**
更新会覆盖 exe。建议在「选项 → 设置 → 更新」里关掉自动检查更新；万一被覆盖，重新对新的 exe 跑一次即可。

**Q：需要 .NET 吗？**
需要 .NET Framework 4.x，Windows 10 / 11 系统自带，不用额外安装。

**Q：补丁会被检测到吗？**
程序文件签名会失效（文件被修改），这是改字节补丁的必然结果。

---

## 目录结构

```
bandizip-div/
├─ src/
│  └─ BandizipProPatch.cs      # 全部源码（C# / WinForms）
├─ docs/                        # 文档截图
├─ build.bat                    # 一键编译（cmd）
├─ build.ps1                    # 一键编译（PowerShell）
├─ .github/workflows/build.yml  # GitHub Actions 自动构建
├─ CHANGELOG.md
├─ LICENSE
└─ README.md
```

---

## 更新日志

见 [CHANGELOG.md](CHANGELOG.md)。

## License

[MIT](LICENSE)

---

> 本项目仅用于逆向工程学习与本地软件研究。使用前请自行确认软件许可条款。

