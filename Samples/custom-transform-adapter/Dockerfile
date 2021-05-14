# golang:1.16-alpine
FROM golang@sha256:49c07aa83790aca732250c2258b5912659df31b6bfa2ab428661bc66833769e1

RUN mkdir /app
ADD . /app
WORKDIR /app
ENV PORT=3000

RUN go build -o main .

EXPOSE $PORT
CMD ["/app/main"]