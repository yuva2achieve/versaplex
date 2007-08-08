#include "wvlogger.h"
#include <wvwin32-sanitize.h>
#include <wvlog.h>
#include <wvlogfile.h>

static WvLog *log;
static WvLogRcv *rcv1, *rcv2;

extern char **_argv;

void wvlog_open()
{
    rcv1 = new WvLogConsole(dup(2));
    rcv2 = new WvLogFile("c:\\temp\\vxodbc.log");
    log = new WvLog(GetCurrentProcessId(), WvLog::Debug);
}


void wvlog_print(const char *file, int line, const char *s)
{
    if (!log)
	wvlog_open();
    log->print("%s:%s: %s", file, line, s);
}


void wvlog_close()
{
    if (log)
	delete log;
    if (rcv1)
	delete rcv1;
    if (rcv2)
	delete rcv2;
    log = NULL;
    rcv1 = rcv2 = NULL;
}
