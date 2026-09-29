---
title: Вход через Telegram
description: Как войти в Laraue Boards через аккаунт Telegram — внутри Telegram Mini App и через виджет входа в браузере.
keywords: [вход через telegram, telegram widget вход, telegram mini app авторизация, вход без пароля telegram]
type: documentation
project: boards
order: 1
createdAt: 2026-04-22
updatedAt: 2026-08-06
---

Laraue Boards использует ваш существующий аккаунт Telegram как учётную запись. Отдельной регистрации, пароля для запоминания или email для подтверждения не требуется.

## Два способа открыть Laraue Boards

### Как Telegram Mini App

Найдите **@msgboard_bot** в Telegram и нажмите **Start**. Mini App откроется внутри Telegram — вы сразу авторизованы через свою учётную запись Telegram. Никакого виджета, редиректа или лишних шагов.

![Кнопка запуска Mini App рядом с чатом бота](https://laraue.com/static/images/blog/docs/laraue-boards/message-board-bot-launch-mini-app-button.jpg)

Это рекомендуемый способ пользоваться Laraue Boards с телефона.

### В браузере

Откройте [boards.laraue.com/login](https://boards.laraue.com/login) в любом браузере. Вы увидите экран входа с кнопкой **Log in with Telegram**. Нажатие на неё открывает всплывающее окно авторизации Telegram — подтвердите его, и вас вернёт обратно в приложение.

![Экран входа в веб-версии с кнопкой Log in with Telegram](https://laraue.com/static/images/blog/docs/laraue-boards/web-login.jpg)

Для веб-версии нужно, чтобы ваш аккаунт Telegram был доступен на этом же устройстве или через веб-клиент Telegram.

## Какие данные используются

При входе Laraue Boards получает ваш Telegram user ID, имя, фамилию (если указана), username (если указан) и URL фото профиля. Сообщения не читаются — для авторизации используется только ваша учётная запись.

Вход проверяется на сервере с помощью криптографического хеша, подписанного токеном бота, так что данные подделать нельзя.

## Переключение аккаунтов

Laraue Boards не поддерживает одновременную работу с несколькими аккаунтами Telegram. Чтобы сменить аккаунт, выйдите и войдите заново с другой учётной записью Telegram.

![Опция выхода из аккаунта в Laraue Boards](https://laraue.com/static/images/blog/docs/laraue-boards/logout.jpg)

## После входа: выбор организации

При первом входе вы попадаете на экран со списком всех организаций, к которым у вас есть доступ, включая **Personal** — вашу личную организацию, настроенную автоматически. Нажмите на одну, чтобы открыть её, или нажмите **+ Create organization**, чтобы создать новую.

![Экран выбора организации со списком Personal, командной организации и кнопкой Create organization](https://laraue.com/static/images/blog/docs/laraue-boards/login-organization.jpg)

Переключаться между организациями можно в любой момент из выпадающего списка в верхней части боковой панели, без выхода из аккаунта. Там показан тот же список, что и сразу после входа.

![Выпадающий список переключения организаций в верхней части боковой панели](https://laraue.com/static/images/blog/docs/laraue-boards/switch-organization.jpg)

## Связанные страницы

- [Организации](/ru/blog/documentation/laraue-boards/concepts/organizations)