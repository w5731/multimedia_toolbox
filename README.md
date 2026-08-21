# 班级多媒体任务看板系统

双端系统:**服务器端**维护一个教师网站(任务管理 / 叫号 / 在线状态 / 通知),**客户端**跑在教室多媒体电脑上(Windows 7),开机自启,把今日任务像壁纸一样显示在桌面上,收到叫号时弹出置顶大窗口并响铃。

设计目标:可靠、好用、简便、稳定。

```
multimedia_toolbox/
├── server/            服务器端 (Node.js + Express + SQLite)
│   ├── server.js / db.js / config.js / util.js / helpers.js
│   ├── middleware/session.js
│   ├── routes/        auth.js(登录) admin.js(教师端API) client.js(客户端API)
│   ├── public/        教师网站(纯 HTML+JS+CSS,无构建步骤)
│   └── data/          SQLite 数据库(首次运行自动创建)
└── client/            客户端 (C# WPF,.NET Framework 4.8,单个 exe)
    ├── MultimediaClient.exe
    ├── install.bat    一键安装(检查 .NET 4.8 并启动)
    ├── build.bat      从源码重新编译(需要本机 csc.exe)
    ├── src/           全部源码(19 个 .cs,无 XAML)
    └── assets/bell.wav  铃声(编译时嵌入 exe)
```

---

## 一、服务器部署

要求:Node.js ≥ 18(推荐 LTS),Linux/Windows 均可。

```bash
cd server
npm install
node server.js          # 默认监听 0.0.0.0:19283,可用环境变量 PORT 改
```

首次启动自动建库建表,并创建默认管理员:

> **账号 `admin` / 密码 `admin123` — 首次登录后请立即在"账号"页修改!**

### 开机自启(二选一)

```bash
# pm2
npm i -g pm2
pm2 start server.js --name multimedia-toolbox
pm2 save && pm2 startup

# systemd (/etc/systemd/system/multimedia-toolbox.service)
[Unit]
Description=Multimedia Toolbox
After=network.target
[Service]
WorkingDirectory=/opt/multimedia_toolbox/server
ExecStart=/usr/bin/node server.js
Restart=always
Environment=PORT=19283
[Install]
WantedBy=multi-user.target
```

### HTTPS(公网部署强烈建议)

用 Caddy 最省事:

```
toolbox.example.com {
    reverse_proxy 127.0.0.1:19283
}
```

纯内网(校园网)使用可保持 HTTP。

### 备份

数据库只有一个文件 `server/data/toolbox.db`,每天复制一份即可:

```bash
# crontab 示例:每天 3:30 备份,保留 14 天
30 3 * * * cp /opt/multimedia_toolbox/server/data/toolbox.db /backup/toolbox-$(date +\%F).db
0 4 * * * find /backup -name 'toolbox-*.db' -mtime +14 -delete
```

### 重置数据

停服后删除 `server/data/` 目录,重启即回到初始状态(重新生成默认 admin)。

---

## 二、教师网站使用

浏览器打开 `http://服务器IP:19283`,登录后:

| 页面 | 功能 |
|---|---|
| 仪表盘 | 各班级客户端在线状态(绿/灰点、最后心跳、版本、IP)、今日任务数、最近叫号 |
| 任务 | 按班级编辑任务;每条=内容+时间点(如 `12:30`)或时间段(`12:30-13:30`);日期模式:**仅一次 / 连续日期范围 / 每周重复(选星期几)/ 每天**;某天任务可一键复制到其他天;可停用/删除 |
| 叫号 | 号数(可一次多个,如 `12, 15, 27`)+ 地点(默认"办公室",有快捷按钮可自定义)+ 原因(可空);历史记录实时显示状态:待接收 → 已显示 → 已关闭,可撤销未接收的叫号 |
| 客户端 | 建班级、生成配对码(6 位,30 分钟内有效)、给客户端命名/换绑班级/解绑;**远程下发设置**:弹窗时长(3-120 秒)、铃声音量、看板位置(右/左/顶)、字号缩放,客户端 3 秒内生效 |
| 通知 | 发布常驻文字(如"今天下午 16:30 提前放学"),显示在看板顶部 |
| 账号 | 改自己的密码;管理员可增删教师账号 |
| 日志 | 审计日志:谁在什么时间登录/发叫号/改任务,全部留痕 |

---

## 三、客户端部署(教室 Win7 一体机)

1. 安装 **.NET Framework 4.8**(Win7 SP1 官方支持):
   <https://dotnet.microsoft.com/download/dotnet-framework/net48>
2. 把 `client/` 里的 `MultimediaClient.exe` 和 `install.bat` 拷到教室电脑(任意目录,如 `C:\MultimediaClient\`)。
3. 双击 `install.bat`:检查运行环境并启动客户端;**以后每次开机自动启动**,无需重复操作。
4. 首次启动弹出**配对窗口**:输入服务器地址(如 `http://192.168.1.10:19283`)和教师网站上生成的 6 位配对码,确定即完成绑定,看板立即显示。

### 看板行为说明

- 半透明深色圆角面板,贴在桌面右侧(可改左侧/顶部),**鼠标完全穿透**——桌面图标、右键菜单一切如常。
- **永远位于壁纸之上、所有应用窗口之下**:老师打开 PPT、白板软件等任何程序都会自然盖住看板,不影响上课;应用关闭后看板自然露出来。Win+D 显示桌面后自动恢复。
- 显示内容:大时钟(时:分:秒)、日期+星期+班级、今日任务时间轴(当前进行中的条目高亮"进行中"、已过的变暗)、下一条倒计时("接下来 12:30 去食堂吃饭 · 还有 8 分钟")、顶部通知栏。
- 断网/服务器宕机时看板照常显示(本地缓存),并在面板上提示"离线";网络恢复自动重连(3s→10s→30s 退避)。
- 教室电脑时钟不准也没关系:客户端以服务器时间为准。

### 叫号弹窗

- 教师叫号后 ≤3 秒弹出置顶大窗:超大号数、目的地、原因,红框呼吸闪烁 + 响铃。
- 默认 10 秒自动关闭(网站可远程改),底部有"关闭"按钮;显示和关闭都会回执给服务器,教师端能看到学生是否已看到。
- **若系统处于静音,会自动取消静音并把音量调到设定值(默认 50%)**,铃响后不会改回原状(避免反复跳变)。

### 日常维护

- 托盘图标(蓝色"看"字):右键 → 设置 / 立即刷新 / 退出。**退出和改设置需要教师密码,默认 `123456`**,可在设置里修改,防止学生误关。
- 日志:`程序目录\logs\client-yyyyMMdd.log`(自动按天分文件,单文件 2MB 截断)。
- 卸载:托盘退出程序 → 删除程序目录 → 注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 删掉 `MultimediaClient` 项。
- 换电脑/重装系统:在网站"客户端"页解绑旧设备,新机器重新配对即可。

---

## 四、技术要点(为什么这样设计)

- **通信用 HTTP 3 秒短轮询,不用 WebSocket**:.NET 4.8 的 ClientWebSocket 在 Win7 上不受支持;轮询无状态、穿透校园网代理、断线自动退避重连,叫号延迟 ≤3 秒足够用。
- **客户端零第三方依赖**:编译产出单个 exe,拷贝即用;铃声嵌入资源;JSON 用内置 JavaScriptSerializer。
- **SQLite WAL 模式**:服务器单进程单文件数据库,无需装 MySQL,备份=复制文件。
- **服务器下发权威时间**:所有"进行中/倒计时"判定用服务器时间,教室机时钟错乱不影响。
- **可靠性细节**:配置/缓存原子写入(先写临时文件再改名)、单实例互斥锁、全局异常兜底写日志不弹报错框、登录限频防爆破、bcrypt 密码哈希、审计日志。

---

## 五、从源码重新编译客户端(可选)

教室里只需 exe,不需要编译。若要改源码:

```
cd client
build.bat        # 调用 C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
```

产出 `MultimediaClient.exe`(约 100KB,含嵌入铃声)。

---

## 六、Win7 验收清单(部署后过一遍)

- [ ] 开机自动启动,看板出现在桌面右侧,大字清晰
- [ ] 桌面图标可点、右键菜单正常(鼠标穿透)
- [ ] 打开任意软件(如 PPT 放映)看板被自然盖住,关闭后恢复
- [ ] 网站叫号:≤3 秒弹窗、响铃、10 秒自动关;教师端状态变"已显示→已关闭"
- [ ] 系统静音状态下叫号:自动取消静音并响铃
- [ ] 断网 1 分钟:看板照常显示并提示离线;恢复网络后自动重连
- [ ] 网站远程改弹窗时长/音量:客户端 3 秒内生效
- [ ] 托盘"退出"需要密码;密码错误退不出
- [ ] 重启电脑:客户端自启且无需重新配对
