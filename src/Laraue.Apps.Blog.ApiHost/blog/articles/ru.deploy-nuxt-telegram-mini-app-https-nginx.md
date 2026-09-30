---
title: Деплой Nuxt Telegram Mini App. Настройка HTTPS на nginx с Let's Encrypt. Новый мини-апп через BotFather
description: Часть 7 цикла о разработке Telegram-таск-трекера в одиночку. Как задеплоить Nuxt Mini App в связке nginx + HTTPS + Let's Encrypt, настроить автопродление сертификатов через certbot, зарегистрировать mini app в BotFather и протестировать его локально через ngrok.
type: article
createdAt: 2026-06-23 12:00
updatedAt: 2026-09-30 07:44
projects: [boards]
tags: [nginx, nuxt, telegram-mini-app, https, lets-encrypt, certbot, ngrok, self-hosting, devlog]
previousLink: deploying-dotnet-postgres-vps-docker-compose
nextLink: telegram-mini-app-authentication-dotnet
---

> **Architecture First: как в одиночку с ИИ сделать альтернативу Jira** — Часть 7.
> В [Предыдущей статье](deploying-dotnet-postgres-vps-docker-compose) Telegram бот был развернут на сервере. Бот занимается только сохранением сообщений пользователя. В этой статье мы задеплоим и настроим пустой Telegram mini app, который превратится в полноценное приложение в следующих итерациях.

Цель: добиться, чтобы Nuxt-приложение открывалось внутри Telegram как Mini App. План: создать приложение, в котором почти не будет логики, задеплоить его на сервер. После — сделать его доступным по HTTPS, зарегистрировать адрес как mini app в `@BotFather`. Убедиться, что приложение открывается и пользовательские данные отображаются.

## Создание Nuxt-приложения

Фронтенд на [Nuxt](https://nuxt.com/) был развернут в новом репозитории [laraue-boards](https://github.com/Laraue/laraue-boards) командой:

```bash
pnpm create nuxt@latest laraue-boards
```

Минимальный запускаемый Nuxt-проект — содержит лишь файловую структуру для будущего приложения и шаблонную страницу в `app.vue`.

В первой версии мы хотим лишь научиться определять, что приложение было запущено из Telegram и вывести данные текущего пользователя. Telegram передаёт в mini app данные через свой SDK. Мы решили читать эти данные через Nuxt-плагин [`auth.init.ts`](https://github.com/Laraue/laraue-boards/blob/master/app/plugins/auth.init.ts), запускающийся до отрисовки приложения:

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

Важное условие — `WebApp.initData !== ''`. Пакет `@twa-dev/sdk` позволяет работать с типизированными данными, полученными Mini App от Telegram: когда приложение запущено внутри Telegram, объект `initData` заполнен, а в обычном браузере — нет. Так приложение и определяет, запущено ли оно как Mini App. Если да — данные из init data устанавливаются в `appState`; если нет — туда устанавливаются данные об ошибке, которая затем покажется на экране.

> Описываемая версия приложения **не проверяет подлинность данных** в init data. В реальной реализации валидация обязательна и будет описываться далее, в статье про [аутентификацию Telegram Mini App](telegram-mini-app-authentication-dotnet).

В первой версии не добавляется никакого интерфейса: весь шаблон `app.vue` сделан так, чтобы просто оценить, корректно ли заполняется объект пользователя:

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

## GitHub CI-пайплайн фронтенда

Пайплайн фронтенда [`build-and-publish.yml`](https://github.com/Laraue/laraue-boards/blob/master/.github/workflows/build-and-publish.yml) билдит SPA Nuxt-приложение и выгружает результат сборки на VPS:

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

Пайплайн устанавливает pnpm и Node, запускает билд командой `pnpm nuxt generate` и, в случае пуша в ветку `master`, — копирует результат по SSH в `/home/laraue/note-to-board-frontend`. Эту папку контейнер nginx смонтирует как свой веб-рут.

Мы используем `nuxt generate`, а не `nuxt build`, чтобы получить полностью статичный сайт с HTML, CSS и JavaScript-файлами. Это позволяет отдавать из nginx обычные файлы, делегируя рендеринг JS клиентской стороне и не тратить ресурсы на запуск Node-процесса. Если какую-то часть контента придется сделать индексируемой для поисковых систем, фронтенд перейдет на серверный рендеринг (SSR) — Nuxt дает возможность сделать это в несколько кликов.

## Разворачивание Mini App на поддомене с HTTPS

Приложение успешно собрано и его файлы отправлены на сервер, нужно открыть к нему доступ извне. Telegram имеет требование: **в Mini App может быть открыт только HTTPS адрес с валидным сертификатом**. Поэтому придется привязать к приложению домен (поддомен в нашем случае) с действующим HTTPS-сертификатом и настроить отдачу файлов приложения по его адресу.

Для приложения был заведен поддомен `boards.laraue.com`, чтобы не смешивать его адреса с адресами основного сайта. В перспективе такой поддомен может обслуживаться отдельным VPS.

Поддомен создаётся обычно в DNS-панели регистратора домена: это дополнительная `A`-запись со значением `msgboard`, указывающая на IP-адрес VPS, который будет обрабатывать запросы. Новая запись может начать работать не сразу — изменения применяются от нескольких минут до заметно большего времени. Это важно, так как **HTTPS-сертификат не получится выпустить, пока поддомен не начнет резолвиться**. Сертификат будет выпускаться через Let's Encrypt и если DNS ещё не обновился, проверка может падать с ошибкой:

```
DNS problem: NXDOMAIN looking up A for boards.laraue.com
  - check that a DNS record exists for this domain;
DNS problem: NXDOMAIN looking up AAAA for boards.laraue.com
  - check that a DNS record exists for this domain
```

Ошибка означает, что имя пока не резолвится — запись DNS не распространилась по сети или неверна. Проверить, что поддомен резолвится, можно через `ping` локально или с помощью бесплатных сервисов для проверки DNS.

## Конфигурация Nginx

Запросы, приходящие на поддомен, обрабатываются через сервер nginx. Он добавляется в файл `docker-compose` как еще один контейнер:

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

**Собранные файлы фронтенда монтируются в nginx как volume.** Строка `./note-to-board-frontend/public:/usr/share/nginx/html/note-to-board-frontend` говорит о том, что файлы фронтенда всегда будут доступны в контейнере по пути `/usr/share/nginx/html/note-to-board-frontend`. Когда новая версия фронтенда залита, контейнер перезагружать не придется — nginx всегда отдает последние выгруженные файлы. То есть деплой фронтенда — это просто замена файлов при пуше в `master`. 

**`command` выполняет перезагрузку nginx по таймеру.** Каждые шесть часов скрипт запускается и выполняет `nginx -s reload`. Перезагрузка нужна, чтобы nginx подхватывал обновлённые TLS-сертификаты: их периодически будет обновлять контейнер с certbot, описанный далее.

Для работы поддомена в `nginx.conf` добавляются два блока. Первый прослушивает порт 80 и дает доступ к папке ACME-challenge, с помощью которой Let's Encrypt будет проверять владение доменом (об этом ниже), и редиректит всё остальное на HTTPS:

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

Второй блок — основной: порт 443, TLS и раздача приложения:

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

Несколько комментариев по конфигурации.

`root` указывает на папку со статикой, прокинутую внутрь контейнера. Nginx раздаёт файлы прямо с диска, без Node-процесса. 

`try_files ... /index.html` — SPA-fallback. SPA занимается роутингом в браузере, поэтому на клиентском роуте nginx должен всегда отдавать файл `index.html` и передавать управление роутеру приложения, а не возвращать 404. Эта строка позволяет работать навигации внутри SPA приложения.

Файлы сборки под `/_nuxt` всегда имеют хеш в имени, поэтому их можно помечать как `immutable` и делать долгое кэширование, чтобы браузер не скачивал их каждый раз при открытии приложения. А `index.html`, напротив, имеет атрибуты `no-store, must-revalidate`, чтобы браузер всегда загружал его свежую копию. Это стандартный паттерн кеширования для SPA: мы одновременно избегаем устаревшего кода и не грузим одни и те же файлы при каждом запуске.

В реальной конфигурации появятся `location`-блоки, для проксирования запросов вида `/api/...` на бэкенд, но они появятся только в [следующей статье](telegram-mini-app-authentication-dotnet).

## TLS-сертификаты через Let's Encrypt и certbot

Конфигурация приложения почти готова, но чтобы запуститься - необходимо получить сертификат. Let's Encrypt позволяет сделать это абсолютно бесплатно. Каждый выпущенный им сертификат имеет срок жизни 3 месяца; чтобы не заниматься выпуском и обновлением вручную — используем certbot.

`certbot` работает в отдельном контейнере в упомянутом ранее файле `docker-compose`, его задача — обновлять сертификаты до того, как их срок годности истечет:

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

Первый volume, `letsencrypt` — место, куда certbot сохраняет выпущенные сертификаты. `nginx` читает из той же директории — поэтому пути `ssl_certificate` в server-блоках выше указывают на `/etc/letsencrypt/live/boards.laraue.com/`.

Второй volume, `acme-challenge` — папка для временных файлов для подтверждения владения доменом: Let's Encrypt просит создать временный файл, который будет доступен по адресу `http://boards.laraue.com/.well-known/acme-challenge/{fileName}`. Если файл доступен, адрес считается подтвержденным и Let's Encrypt выпускает сертификат. Порт 80 раздаёт этот путь напрямую, а не делает редирект на HTTPS — challenge должен отрабатывать и до того момента, когда появляется сертификат.

`entrypoint` — цикл, запускающий `certbot renew` каждые двенадцать часов. Когда время жизни сертификата подходит к концу, данная команда выпустит новый сертификат автоматически.

Главная проблема в этой схеме — первое получение сертификата. `certbot renew` может выполнить обновление TLS, только когда он уже существует. Поэтому для получения первого сертификата нужно использовать другой подход.

### Проблема курицы и яйца: nginx не стартует без сертификата, сертификат не получить без nginx

Server-блок порта 443 ссылается на файлы сертификата через `ssl_certificate` и `ssl_certificate_key`. Если этих файлов нет, **nginx отказывается стартовать**. Но сертификат нельзя получить, пока nginx не запущен и не дает доступа к папке с ACME-challenge.

Поэтому первый сертификат получают однократным запуском скрипта [`init-letsencrypt.sh`](https://github.com/wmnnd/nginx-certbot/blob/master/init-letsencrypt.sh), который:

1. Создаёт **заглушку — самоподписанный сертификат** по пути, который ожидает nginx.
2. С этим файлом nginx **успешно стартует** — поднимаются блок порта 80 с ACME-challenge и блок порта 443 с заглушкой.
3. **Заглушка удаляется и скрипт запрашивает настоящий сертификат** у Let's Encrypt, который проходит ACME-challenge через запущенный nginx.
4. Скрипт **перезагружает nginx**, и тот подхватывает настоящий сертификат.

Скрипт больше не нужен — обновления сертификата certbot берёт на себя.

На этом этапе приложение запускается по HTTPS и становится доступным по адресу `boards.laraue.com`. Открытие URL в браузере показывает болванку-приложение с ошибкой о том, что приложение запущено вне Telegram. Остается один шаг, перед тем как оно сможет стать Mini App в Telegram.

## Регистрация Mini App в BotFather

Необходимо уведомить Telegram, что наш бот имеет Mini App, доступный по адресу `boards.laraue.com`. Это делается с помощью [@BotFather](https://t.me/BotFather): необходимо выбрать бота, и в разделе **Mini Apps** привязать приложение как **Main App** или к **menu button** бота (кнопке рядом с полем ввода). В обоих случаях потребуется ввести адрес приложения: `https://boards.laraue.com`. В Laraue Boards Mini App привязан к menu button.

После этой настройки рядом с чатом бота появится кнопка запуска приложения:

![Кнопка запуска Mini App рядом с чатом бота](https://laraue.com/static/images/blog/articles/laraue-boards/message-board-bot-launch-mini-app-button.jpg)

По нажатию на кнопку Nuxt-приложение откроется прямо в Telegram, плагин `auth.init.ts` прочитает init data и выведет на экран объект пользователя, или ошибку инициализации, если что-то пошло не так.

## Локальная разработка Mini App через ngrok

Требование HTTPS добавляет сложностей при локальной разработке: Telegram не откроет Mini App с `http://localhost:3000`. Мы используем — [ngrok](https://ngrok.com/), чтобы обойти это ограничение. `ngrok` создает публичный HTTPS-URL, туннелирующий запросы на локальный компьютер. Полную настройку можно увидеть в [README](https://github.com/Laraue/laraue-boards) репозитория фронтенда, но общий алгоритм такой:

1. **Затуннелить локальные порты.** Конфиг `ngrok.yml` выставляет фронтенд (3000) и бэкенд (5200) как публичные HTTPS-URL через `ngrok start --all`.
2. **Настроить бота на адреса, выданные ngrok.** В настройках Mini App в BotFather кнопка ставится на ngrok-URL фронтенда.
3. **Разрешить бэкенду принимать запросы с адреса, выданного ngrok.** ngrok-URL фронтенда добавляется в CORS-allowlist бэкенда, базовый адрес API в фронтенде меняется на ngrok-URL бэкенда.

Таким образом можно выполнять полное тестирование Mini App с локальной машины.

Небольшой совет: внутри Telegram нет консоли браузера, и для отладки мы делали инъекцию скрипта `eruda`. Скрипт добавляет кнопку открытия консоли прямо внутри Mini App — это очень полезно, когда при запуске Mini App видишь только белый экран и не понимаешь, что сломалось.

## Итоги

Продуктовых фич на этом этапе не появилось — но был добавлен фундамент, на котором будет разрабатываться весь фронтенд. Nuxt-приложение обновляется на сервере по пушу в `master` и остается только добавлять в него код.

## Что дальше

Mini App успешно открывается, но пока ничего не умеет: у него нет бэкенда, и данные пользователя могут быть подделаны. Следующая статья о добавлении web API: нового хоста, который провалидирует пользователя из Telegram Mini App, и вернет в приложение первые реальные данные.