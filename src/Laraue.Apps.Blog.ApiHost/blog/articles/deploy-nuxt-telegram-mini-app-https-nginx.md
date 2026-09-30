---
title: Deploying a Nuxt Telegram Mini App. Setting up HTTPS on nginx with Let's Encrypt. A new mini app via BotFather
description: Part 7 of building a Telegram task tracker solo. How to deploy a Nuxt Mini App with the nginx + HTTPS + Let's Encrypt combo, set up automatic certificate renewal with certbot, register the mini app in BotFather, and test it locally through ngrok.
type: article
createdAt: 2026-06-23 12:00
updatedAt: 2026-09-30 07:44
projects: [boards]
tags: [nginx, nuxt, telegram-mini-app, https, lets-encrypt, certbot, ngrok, self-hosting, devlog]
previousLink: deploying-dotnet-postgres-vps-docker-compose
nextLink: telegram-mini-app-authentication-dotnet
---

> **Architecture First: Building a Jira Alternative Solo, AI-Assisted** — Part 7.
> In the [previous article](deploying-dotnet-postgres-vps-docker-compose) the Telegram bot was deployed to the server. The bot only saves the user's messages. In this article we deploy and configure an empty Telegram mini app, which will turn into a full application over the next iterations.

The goal: get a Nuxt app to open inside Telegram as a Mini App. The plan: create an app with almost no logic in it, deploy it to the server. After that — make it reachable over HTTPS, register the address as a mini app in `@BotFather`. Make sure the app opens and the user's data is displayed.

## Creating the Nuxt app

The [Nuxt](https://nuxt.com/) frontend was set up in a new repository, [laraue-boards](https://github.com/Laraue/laraue-boards), with the command:

```bash
pnpm create nuxt@latest laraue-boards
```

A minimal runnable Nuxt project — it contains only the file structure for the future app and a template page in `app.vue`.

In the first version we only want to learn to detect that the app was launched from Telegram, and display the current user's data. Telegram passes data into the mini app through its SDK. We decided to read that data in the Nuxt plugin [`auth.init.ts`](https://github.com/Laraue/laraue-boards/blob/master/app/plugins/auth.init.ts), which runs before the app renders:

```ts
export default defineNuxtPlugin(async (nuxtApp) => {
  const { setIsAppInitialized, setIsInMiniApp } = useAppState();
  const { setUser } = useAuth();
  try {
    const WebApp = (await import('@twa-dev/sdk')).default;
    const isInMiniApp = WebApp.initData !== '';
    if (isInMiniApp) {
      setIsInMiniApp(true);
      setUser(WebApp.initData);
    } else {
      throw Error('Init data object is missing');
    }
  } catch (err) {
    const { setInitError } = useAppState();
    setInitError(err);
  } finally {
    setIsAppInitialized(true);
  }
});
```

The important condition is `WebApp.initData !== ''`. The `@twa-dev/sdk` package gives typed access to the data the Mini App receives from Telegram: when the app is launched inside Telegram, the `initData` object is filled, and in a regular browser it is not. That is how the app determines whether it is running as a Mini App. If yes — the data from init data is set into `appState`; if not — error data is set there instead, and it will be shown on the screen.

> The version described here **does not verify the authenticity** of the init data. In a real implementation, validation is mandatory, and it will be described later, in the article about [Telegram Mini App authentication](telegram-mini-app-authentication-dotnet).

The first version adds no interface at all: the whole `app.vue` template exists just to check whether the user object is filled correctly:

```vue
<template>
  <div id="app">
    <div v-if="initError">
      {{ initError }}
    </div>
    <div v-else>
      {{ appState.user }}
    </div>
  </div>
</template>
```

## The frontend's GitHub CI pipeline

The frontend pipeline, [`build-and-publish.yml`](https://github.com/Laraue/laraue-boards/blob/master/.github/workflows/build-and-publish.yml), builds the Nuxt SPA and uploads the build result to the VPS:

```yaml
name: Build Vue App
on:
  push:
    branches: [ "master" ]
  pull_request:
    branches: [ "master" ]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Install pnpm
        uses: pnpm/action-setup@v4
        with:
          version: 10
      - name: Use Node.js
        uses: actions/setup-node@v4
        with:
          node-version: '20.x'
          cache: 'pnpm'
      - run: pnpm i
      - run: pnpm nuxt generate
      - name: Upload
        if: github.ref == 'refs/heads/master'
        uses: marcodallasanta/ssh-scp-deploy@v1.2.0
        with:
          host: ${{ secrets.SSH_HOST }}
          port: ${{ secrets.SSH_PORT }}
          user: ${{ secrets.SSH_USER }}
          password: ${{ secrets.SSH_PASSWORD }}
          local: .output/*
          remote: "/home/laraue/note-to-board-frontend"
          post_upload: echo "Uploaded"
```

The pipeline installs pnpm and Node, runs the build with `pnpm nuxt generate` and, in the case of a push to `master`, copies the result over SSH to `/home/laraue/note-to-board-frontend`. The nginx container will mount that folder as its web root.

We use `nuxt generate`, not `nuxt build`, to get a fully static site of HTML, CSS and JavaScript files. This lets nginx serve plain files, delegating the JS rendering to the client side, and avoids spending resources on running a Node process. If some part of the content ever has to become indexable by search engines, the frontend will move to server-side rendering (SSR) — Nuxt makes that possible in a few clicks.

## Serving the Mini App on a subdomain with HTTPS

The app is built and its files are on the server; now it needs to be reachable from the outside. Telegram has a requirement: **a Mini App can only open an HTTPS address with a valid certificate**. So the app has to be bound to a domain (a subdomain in our case) with a working HTTPS certificate, and serving the app's files at that address has to be configured.

The subdomain `boards.laraue.com` was created for the app, to keep its addresses separate from the main site's. Down the line, such a subdomain could even be served by a separate VPS.

A subdomain is usually created in the domain registrar's DNS panel: it is an extra `A` record with the value `boards`, pointing at the IP address of the VPS that will handle the requests. A new record may not take effect immediately — the changes apply within anywhere from a few minutes to noticeably longer. This matters, because **the HTTPS certificate cannot be issued until the subdomain starts resolving**. The certificate will be issued through Let's Encrypt, and if the DNS has not updated yet, the check can fail with this error:

```
DNS problem: NXDOMAIN looking up A for boards.laraue.com
  - check that a DNS record exists for this domain;
DNS problem: NXDOMAIN looking up AAAA for boards.laraue.com
  - check that a DNS record exists for this domain
```

The error means the name does not resolve yet — the DNS record has not propagated or is wrong. You can check that the subdomain resolves with a local `ping`, or with the free DNS-checking services.

## The nginx configuration

Requests arriving at the subdomain are handled by an nginx server. It is added to the `docker-compose` file as one more container:

```yaml
nginx:
  ports:
    - "80:80"
    - "443:443"
  build:
    context: .
    dockerfile: "NginxDockerfile"
  networks:
    - dockerapi-dev
  volumes:
    - ./../letsencrypt:/etc/letsencrypt
    - ./../.well-known/acme-challenge:/.well-known/acme-challenge
    - ./note-to-board-frontend/public:/usr/share/nginx/html/note-to-board-frontend
  command: "/bin/sh -c 'while :; do sleep 6h & wait $${!}; nginx -s reload; done & nginx -g \"daemon off;\"'"
```

**The built frontend files are mounted into nginx as a volume.** The line `./note-to-board-frontend/public:/usr/share/nginx/html/note-to-board-frontend` means the frontend files are always available inside the container at `/usr/share/nginx/html/note-to-board-frontend`. When a new frontend version is uploaded, the container does not need to be restarted — nginx always serves the latest uploaded files. So a frontend deploy is just a file replacement on a push to `master`.

**`command` reloads nginx on a timer.** Every six hours the script runs and executes `nginx -s reload`. The reload is needed so nginx picks up renewed TLS certificates: those will be periodically renewed by the certbot container, described further down.

For the subdomain to work, two blocks are added to `nginx.conf`. The first listens on port 80, gives access to the ACME-challenge folder that Let's Encrypt will use to verify domain ownership (more on that below), and redirects everything else to HTTPS:

```nginx
server {
    listen 80;
    server_name boards.laraue.com;
    location /.well-known/acme-challenge/ {
        root /.well-known/acme-challenge;
    }
    location / {
        return 301 https://$host$request_uri;
    }
}
```

The second block is the main one: port 443, TLS, and serving the app:

```nginx
server {
    listen 443 ssl;
    server_name boards.laraue.com;

    ssl_certificate     /etc/letsencrypt/live/boards.laraue.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/boards.laraue.com/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-GCM-SHA256:ECDHE-ECDSA-AES256-GCM-SHA384:ECDHE-RSA-AES256-GCM-SHA384;
    ssl_prefer_server_ciphers off;

    root /usr/share/nginx/html/note-to-board-frontend;
    index index.html;

    location /_nuxt {
        expires 1y;
        add_header Cache-Control "public, immutable";
        try_files $uri =404;
    }

    location / {
        try_files $uri $uri/ /index.html;

        add_header Cache-Control "no-store, must-revalidate";
        add_header Pragma "no-cache";
        add_header Expires 0;
    }

    location ~* \.(js|css|png|jpg|jpeg|gif|ico|svg|woff|woff2|ttf|eot|json)$ {
        expires 1y;
        add_header Cache-Control "public, immutable";
        try_files $uri =404;
    }
}
```

A few comments on the configuration.

`root` points at the static folder mounted into the container. Nginx serves the files straight from disk, with no Node process.

`try_files ... /index.html` is the SPA fallback. An SPA handles its routing in the browser, so on a client-side route nginx must always return the `index.html` file and hand control to the app's router, rather than returning a 404. This line is what makes navigation inside the SPA work.

The build files under `/_nuxt` always have a hash in their name, so they can be marked `immutable` and cached for a long time, so the browser does not download them on every launch of the app. `index.html`, on the contrary, has the `no-store, must-revalidate` attributes, so the browser always loads a fresh copy of it. This is the standard caching pattern for an SPA: we avoid stale code and avoid downloading the same files on every launch at the same time.

In the real configuration there will also be `location` blocks for proxying `/api/...` requests to the backend, but they only appear in the [next article](telegram-mini-app-authentication-dotnet).

## TLS certificates through Let's Encrypt and certbot

The app's configuration is almost ready, but to start it we need to get a certificate. Let's Encrypt lets us do that completely free. Every certificate it issues has a lifetime of 3 months; to avoid issuing and renewing them by hand, we use certbot.

`certbot` runs in a separate container in the `docker-compose` file mentioned earlier; its job is to renew the certificates before they expire:

```yaml
certbot:
  image: certbot/certbot:v1.7.0
  networks:
    - dockerapi-dev
  volumes:
    - ./../letsencrypt:/etc/letsencrypt
    - ./../.well-known/acme-challenge:/var/www/certbot
  entrypoint: "/bin/sh -c 'trap exit TERM; while :; do certbot renew; sleep 12h & wait $${!}; done;'"
```

The first volume, `letsencrypt`, is where certbot saves the issued certificates. `nginx` reads from the same directory — that is why the `ssl_certificate` paths in the server blocks above point to `/etc/letsencrypt/live/boards.laraue.com/`.

The second volume, `acme-challenge`, is the folder for the temporary files that confirm domain ownership: Let's Encrypt asks to create a temporary file that must be reachable at `http://boards.laraue.com/.well-known/acme-challenge/{fileName}`. If the file is reachable, the address is considered confirmed and Let's Encrypt issues the certificate. Port 80 serves that path directly instead of redirecting it to HTTPS — the challenge has to work even before a certificate exists.

`entrypoint` is a loop that runs `certbot renew` every twelve hours. When a certificate's lifetime is coming to an end, this command will issue a new one automatically.

The main problem in this scheme is getting the certificate the first time. `certbot renew` can only renew a TLS certificate that already exists. So the first certificate has to be obtained through a different approach.

### The chicken-and-egg problem: nginx does not start without a certificate, the certificate cannot be obtained without nginx

The port 443 server block references the certificate files through `ssl_certificate` and `ssl_certificate_key`. If those files do not exist, **nginx refuses to start**. But the certificate cannot be obtained while nginx is not running and not giving access to the ACME-challenge folder.

So the first certificate is obtained with a one-time run of the [`init-letsencrypt.sh`](https://github.com/wmnnd/nginx-certbot/blob/master/init-letsencrypt.sh) script, which:

1. Creates a **stub — a self-signed certificate** at the path nginx expects.
2. With that file in place, nginx **starts successfully** — both the port 80 block with the ACME challenge and the port 443 block with the stub come up.
3. **The stub is deleted and the script requests a real certificate** from Let's Encrypt, which passes the ACME challenge through the running nginx.
4. The script **reloads nginx**, and it picks up the real certificate.

The script is not needed anymore after that — certbot takes over the certificate renewals.

At this point the app runs over HTTPS and becomes reachable at `boards.laraue.com`. Opening the URL in a browser shows the stub app with an error saying it was launched outside Telegram. One step remains before it can become a Mini App in Telegram.

## Registering the Mini App in BotFather

Telegram has to be notified that our bot has a Mini App, available at `boards.laraue.com`. This is done through [@BotFather](https://t.me/BotFather): pick your bot, and in the **Mini Apps** section bind the app as the **Main App** or to the bot's **menu button** (the button next to the input field). In both cases you will need to enter the app's address: `https://boards.laraue.com`. In Laraue Boards the Mini App is bound to the menu button.

After this setting, a launch button appears next to the bot's chat:

![The Mini App launch button next to the bot's chat](https://laraue.com/static/images/blog/articles/laraue-boards/message-board-bot-launch-mini-app-button.jpg)

On tapping the button, the Nuxt app opens right inside Telegram, the `auth.init.ts` plugin reads the init data and prints the user object to the screen — or the initialization error, if something went wrong.

## Local Mini App development through ngrok

The HTTPS requirement adds difficulty to local development: Telegram will not open a Mini App from `http://localhost:3000`. We use [ngrok](https://ngrok.com/) to get around this restriction. `ngrok` creates a public HTTPS URL that tunnels requests to the local computer. The full setup can be seen in the frontend repository's [README](https://github.com/Laraue/laraue-boards), but the general algorithm is:

1. **Tunnel the local ports.** The `ngrok.yml` config exposes the frontend (3000) and the backend (5200) as public HTTPS URLs, via `ngrok start --all`.
2. **Point the bot at the addresses ngrok issued.** In the Mini App settings in BotFather, the button is set to the frontend's ngrok URL.
3. **Allow the backend to accept requests from the address ngrok issued.** The frontend's ngrok URL is added to the backend's CORS allowlist, and the frontend's API base address is changed to the backend's ngrok URL.

This way, full testing of the Mini App can be done from the local machine.

A small tip: there is no browser console inside Telegram, so for debugging we injected the `eruda` script. The script adds a button that opens a console right inside the Mini App — very useful when launching the Mini App shows you nothing but a white screen and you have no idea what broke.

## Conclusions

No product features appeared at this stage — but the foundation the whole frontend will be developed on was added. The Nuxt app updates on the server on a push to `master`, and all that is left is to add code to it.

## What comes next

The Mini App opens successfully, but cannot do anything yet: it has no backend, and the user's data can be forged. The next article is about adding the web API: a new host that will validate the user from the Telegram Mini App and return the first real data to the app.