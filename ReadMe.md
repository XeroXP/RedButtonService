# RedButtonService

It is a windows service that erase data by multiple triggers.

## Installing

Requires `.NET 8`. [click](https://dotnet.microsoft.com/ru-ru/download/dotnet/8.0)

Install `.NET`, then just install msi from [releases page](../../releases) (or build installer yourself).

Installation directory - `%ProgramFiles%\XeroXP\RedButtonService\`

## Configuration

All configuration of service is in json files:

- *appsettings.json*
- *service.json*

### service.json

Main config:

```
{
  "Telegram": {
    "Token": "", //Telegram bot token from @BotFather
    "AdminIds": [ "111111111" ] //Ids of admins that can use the most sensitive commands
  },
  "USBTrigger": {
    "FileName": "erase", //Name of the file that service will be search (and trigger erase if not found)
    "TimeCheckSeconds": 60 //A repeating period of time after which the program searches for a file
  },
  "UserLogonTrigger": {
    "Usernames": [ "SpecialUser" ] //Windows usernames whose login will trigger erase
  },
  "Eraser": {
    "MaxTasks": 3, //USBTrigger can trigger erase event each second - this field can stop it (only 3 erase tasks can be in queue)
    "TimeStatusSendMinutes": 5, //Status of running erase task can be sent in telegram, and this is the period of time when it sends (0 - if you don't want to receive that status)
    "ToErase": [ //Erase task that can contain many files, directories or drives
      {
        "Type": "File",
        "File": "F:\\test.txt"
      },
      {
        "Type": "Dir",
        "Dir": "F:\\test"
      },
      {
        "Type": "RecycleBin"
      },
      {
        "Type": "Unused",
        "Drive": "E:\\"
      },
      {
        "Type": "Drive",
        "Drive": "E:\\"
      },
      {
        "Type": "Drive",
        "VolumeId": "\\\\?\\Volume{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}\\"
      }
    ]
  }
}
```

> Example

```
{
  "Telegram": null, //we can disable telegram
  "USBTrigger": null, //we can disable usb trigger
  "UserLogonTrigger": { //we can also disable user logon trigger by setting null, but one of the triggers needs to be left, otherwise there will be no point in the service
    "Usernames": [ "SpecialUser" ]
  },
  "Eraser": {
    ...
  }
}
```

## Telegram bot

You can receive all erase trigger events and statuses.

Bot have some commands to control over service.

### Commands

- */help*    - help
- */debug*   - send debug info
- */erase*   - trigger erase
- */cancel*  - cancel running erase task
- */disable* - disable erase
- */enable*  - enable erase
- */log_off* - log off all windows sessions

## Credits

Core:

- [cklutz/LockCheck](https://github.com/cklutz/LockCheck)
- [Eraser](https://sourceforge.net/p/eraser/code/HEAD/tree/)

## Contributors

[XeroXP](../../../).
