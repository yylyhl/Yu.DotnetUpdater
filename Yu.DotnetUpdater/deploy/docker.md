# 【1.搭建 Docker 的仓库】
## Add Docker's official GPG key:
```
sudo apt update
sudo apt install ca-certificates curl
sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc
```
## Add the repository to Apt sources:
```
sudo tee /etc/apt/sources.list.d/docker.sources <<EOF
Types: deb
URIs: https://download.docker.com/linux/ubuntu
Suites: $(. /etc/os-release && echo "${UBUNTU_CODENAME:-$VERSION_CODENAME}")
Components: stable
Architectures: $(dpkg --print-architecture)
Signed-By: /etc/apt/keyrings/docker.asc
EOF
sudo apt update
```

# 【2.装 Docker 包】
```
sudo apt install docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo systemctl status docker
sudo systemctl start docker
sudo docker run hello-world
```

# 【3.构建镜像部署流程（在源码根目录[Dockerfile]）】
```
# 进入目录打包镜像
cd D:\Code\slnPath
cd D:\Code\slnPath\myapp
```
### 3.1.1本地构建后上传服务器，服务器导入镜像
```
# 构建镜像：在解决方案同级目录执行 build，指定 Dockerfile 路径。
docker build --progress plain -f ./myapp/Dockerfile -t myapp:latest -t myapp:1.0.0 .
# 将镜像导出为文件
docker save -o myapp.tar myapp:1.0.0
docker save myapp:1.0.0 | gzip > myapp.tar.gz
# 上传至服务器，然后导入镜像：
scp myapp.tar root@192.168.1.11:/home/deploy/images/
docker load --input myapp.tar
docker load -i myapp.tar
# 确认镜像是否成功加载：
docker images | grep myapp
```

### 3.1.2.本地构建推送仓库，服务器拉取
```
# 构建镜像：在解决方案同级目录执行 build，指定 Dockerfile 路径。
docker build --progress plain -f ./myapp/Dockerfile -t registry.example.com/myimages/myapp:1.0.0 -t registry.example.com/myimages/myapp:latest
# 或
VERSION="v1.0.9"
docker build --progress plain -f ./myapp/Dockerfile -t registry.example.com/myimages/myapp:${VERSION} .
docker tag registry.example.com/myimages/myapp:${VERSION} registry.example.com/myimages/myapp:latest

# 本地登录镜像仓库
echo "your_password" | docker login registry.example.com --username your_username --password-stdin
# 推送镜像
docker push registry.example.com/myimages/myapp:${VERSION}
docker push registry.example.com/myimages/myapp:latest

# 服务器登录镜像仓库
echo "your_password" | docker login registry.example.com --username your_username --password-stdin
## 拉新镜像
docker pull registry.example.com/myimages/myapp:latest
```
### 3.2.重建容器启动
```
docker stop deploy-myapp1-1 && docker rm deploy-myapp1-1
#挂载宿主机目录到/app目录，会遮蔽镜像内/app原有的内容
docker run -d \
  --name container-myapp1-1 \
  --restart unless-stopped \
  -p 8082:8080 \
  -v /home/deploy/logs/myapp1:/app/logs \
  -v /home/deploy/config/myapp1:/app/config:ro \
  -v /etc/localtime:/etc/localtime:ro \
  -e TZ=Asia/Shanghai \
  -e ASPNETCORE_URLS="http://+:8080" \
  myapp:latest
```

# 【 Docker 命令】

### 列出全部镜像:
docker images/docker image ls
### 显示镜像完整信息(含分层、环境变量、构建历史):
docker inspect aspnet:latest
### 镜像构建历史:
docker history redis:latest
### 删除指定镜像:
docker rmi myapp:latest
### 删除<none>悬空镜像（构建残留、无标签、无引用）:
docker image prune
### 清理所有未被使用的镜像(谨慎):
docker image prune -a
### 给镜像新增别名:docker tag SOURCE_IMAGE[:TAG] TARGET_IMAGE[:TAG]
docker tag myapp:latest myapp:v1.0.0
### 导出镜像
docker save -o myapp.tar myapp:1.0.0
docker save myapp:1.0.0 | gzip > myapp.tar.gz
### 导入镜像：
docker load --input myapp.tar
docker load -i myapp.tar
### 使用Dockerfile构建，打标签:
```
docker build --progress plain -f ./myapp/Dockerfile -t myapp:latest -t myapp:v1.0.0 .

```
### 查看容器使用的镜像ID:
docker ps --format "table {{.Names}}\t{{.Image}}\t{{.Status}}"
### 查看容器详细信息（挂载卷、环境变量、启动参数）:
docker inspect container-myapp1-1
### 列出所有容器:
docker ps -a
### 只看运行中的:
docker ps
### 容器是否还存在:
docker ps -a | grep container-myapp1-1
### 容器的日志输出:
docker logs container-myapp1-1
### 进入容器内部看：
```
docker exec -it 容器id/容器名称 ls /app
docker exec -it 容器id/容器名称 cat /app/appsettings.json
```
### 停止容器，给容器10秒时间优雅退出:
docker stop container-myapp1-1
### 重启容器:
docker restart container-myapp1-1
### 强制停止并删除容器:
docker rm -f net10-app1 container-myapp1-1
### 启动旧容器:
docker start container-myapp1-1
### 启动新容器:
```
#挂载整个/app目录，会遮蔽镜像内原有的/app内容
docker run -d \
  --name container-myapp1-1 \
  --restart unless-stopped \
  -p 8082:8080 \
  -v /home/deploy/config/myapp1:/app/config:ro \
  -v /home/deploy/config/common:/app/config-com:ro \
  -v /home/deploy/logs/myapp1:/app/logs \
  -v /etc/localtime:/etc/localtime:ro \
  -e TZ=Asia/Shanghai \
  -e ASPNETCORE_URLS="http://+:8080" \
  myapp:latest
```



