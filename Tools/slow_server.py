import http.server, time

class Slow(http.server.SimpleHTTPRequestHandler):
    def copyfile(self, source, outputfile):
        while chunk := source.read(64 * 1024):
            outputfile.write(chunk)
            outputfile.flush()
            time.sleep(0.1)

http.server.ThreadingHTTPServer(("", 8000), Slow).serve_forever()
