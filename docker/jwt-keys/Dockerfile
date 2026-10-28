# syntax=docker/dockerfile:1
# One-shot key generator that runs as a non-root user. A new named volume copies the ownership of
# the directory it is mounted over, so pre-creating the two directories as uid 1654 (the "app"
# user of the .NET images) lets us skip root and chown entirely.
FROM alpine/openssl:3.5.4@sha256:42c7389ef077aed0eb4e96d0abbd094083d701bbaff1313073b061c0c9cd8278
RUN mkdir /jwt-private /jwt-public \
    && chown 1654:1654 /jwt-private /jwt-public \
    && chmod 700 /jwt-private \
    && chmod 755 /jwt-public
USER 1654:1654
