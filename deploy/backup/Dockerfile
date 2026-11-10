FROM alpine:3.21
RUN apk add --no-cache postgresql17-client rclone tzdata bash
ENV TZ=Asia/Jakarta
COPY backup.sh restore.sh entrypoint.sh /usr/local/bin/
RUN chmod +x /usr/local/bin/*.sh
ENTRYPOINT ["entrypoint.sh"]
