创建新工程分两部分：工程本身（dotnet 模板 + csproj 设置）和 Git 设置（初始化 + 首次推送）。

0. 先避开一个坑
e:\C# 现在本身就是 csharpprj 仓库的根目录。新工程不要建在它里面，否则会被这个仓库当作一部分跟踪（或变成嵌套仓库）。放到同级独立目录，例如 E:\Projects\MyApp。

1. 生成工程

cd E:\Projects
dotnet new wpf -n MyApp          # 生成 E:\Projects\MyApp\MyApp.csproj
cd MyApp
dotnet new gitignore             # 生成 .gitignore（含 bin/obj）
dotnet new sln -n MyApp          # 可选，方便 VS 打开
dotnet sln add MyApp.csproj

dotnet new wpf 的默认 TFM 就是 net10.0-windows（本机 SDK 10.0.403）。可用模板有 wpf、wpflib、wpfusercontrollib、wpfcustomcontrollib。

用 Visual Studio 的话：文件 → 新建 → 项目 → 「WPF 应用程序」，然后「Git → 创建 Git 存储库」。

2. csproj 设置（与现有工程保持一致）

<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
</Project>

按需追加的常用项：

设置	作用
<RootNamespace>MyApp</RootNamespace>	命名空间（模板默认已设）
<AssemblyName>MyApp</AssemblyName>	输出 exe 名称
<ApplicationIcon>app.ico</ApplicationIcon>	程序图标
<Version>1.0.0</Version>	版本号
<Product> / <Company>	exe 属性里的产品/公司名
<Platforms>x64</Platforms>	限定平台
3. 构建验证

dotnet build
dotnet run

发布单个 exe（可选）：
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true

4. Git 全局设置（每台机器只需一次）
git config --global user.name "ungtion"
git config --global user.email "1493467827@qq.com"
git config --global init.defaultBranch main
git config --global core.autocrlf true

5. 初始化仓库并首次推送
cd E:\Projects\MyApp
git init -b main
git add -A
git commit -m "Initial commit"

然后建 GitHub 仓库 —— 本机没装 gh，两种方式：

A. 网页手建（不要勾选 README / .gitignore，避免首次推送冲突）

打开 https://github.com/new → 仓库名填 MyApp → Create。

B. 我帮你用 API 建（和上次建 csharpprj 一样，用已保存的 GCM 凭据）
git remote add origin https://github.com/ungtion/MyApp.git
git push -u origin main

推送后 git push 即可（main 已跟踪 origin/main）。日常更新流程和 githun.md 里写的一样：git pull → 改代码 → dotnet build → git add -A → git commit → git push。

关键点小结
一个工程一个目录、一个 Git 仓库，别把新工程塞进已有仓库目录。
.gitignore 一定要在第一次 commit 之前生成，否则 bin/obj 已经进了历史，后面加 ignore 也去不掉。
新建 GitHub 仓库时不要勾初始化文件，否则首次 push 需要 git pull --rebase 先合并。
csproj 里 UseWPF=true + OutputType=WinExe 是 WPF 桌面程序的必需项。
要我现在直接按「E:\Projects\MyApp + 推送到 GitHub ungtion/MyApp」建一个空白 WPF 工程吗？告诉我工程名（以及是否公开）我就动手。cd E:\Projects
dotnet new wpf -n MyApp          # 生成 E:\Projects\MyApp\MyApp.csproj
cd MyApp
dotnet new gitignore             # 生成 .gitignore（含 bin/obj）
dotnet new sln -n MyApp          # 可选，方便 VS 打开
dotnet sln add MyApp.csproj


==============================================================================================

工作区当前是干净的（## main...github/main，无未提交改动），所以后续更新就是标准的 git 流程。

日常更新三步
在 e:\C# 目录下：
# 1. 先同步远端（多人/多机编辑过就必须先做）
git pull

# 2. 改完代码，先确认能编译
dotnet build

# 3. 提交并推送
git add -A
git commit -m "说明你改了什么"
git push

因为 main 已经跟踪 github/main，git push 默认就是推到 GitHub。

常用命令对照
目的	命令
看改了哪些文件	git status
看具体改了什么	git diff
只提交部分文件	git add MainWindow.xaml MainWindow.xaml.cs
看提交历史	git --no-pager log --oneline -10
撤销未提交的改动	git restore MainWindow.xaml
改完提交信息写错了	git commit --amend -m "新的说明" （未推送前才安全）
两个 remote 的区别
remote	地址	推送命令
github	github.com/ungtion/csharpprj	git push 或 git push github main
origin	192.168.92.56:9527/sys/cashrp	git push origin main
想两边都同步，就依次执行两个 git push。如果只维护 GitHub，也可以把内网 remote 删掉：git remote remove origin。

容易踩的坑
bin/ 和 obj/ 已被 .gitignore 忽略，不用手动排除；如果你新增了配置/密钥文件，记得先加进 .gitignore，别把密码、令牌提交上去。
新增的文件必须 git add，否则不会进提交（用 git add -A 最省事）。
push 被拒（rejected） 说明远端有你本地没有的提交：先 git pull --rebase 再 git push。
MainWindow.xaml.cs 等文件被运行时锁定 导致 build 失败时，先关掉正在运行的程序（Get-Process CSharpProject | Stop-Process -Id $_.Id）。注意这里用 -Id 指定进程。
提交前跑一次 dotnet build，避免把编译不过的版本推上去。当前项目构建是 0 error / 0 warning。
需要的话我可以帮你加一个 push.ps1 一键脚本（自动 build → add → commit → pull --rebase → push），或者把两个 remote 的推送合并成一条命令。

11:26 AM
11s
•
DeepSeek/DeepSeek V4.1 Flash (deepseek-flas