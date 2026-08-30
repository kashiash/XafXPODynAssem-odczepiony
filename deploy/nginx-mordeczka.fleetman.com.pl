# XafXPODynAssem — kontener `xpodyn`, 127.0.0.1:8087.
# Na razie TYLKO HTTP: rekord DNS mordeczka.fleetman.com.pl jeszcze nie istnieje,
# wiec certbot nie ma jak wystawic certyfikatu. Blok `listen 443` dopisac
# dopiero po wystawieniu certyfikatu (wzorzec: demo.fleetman.com.pl).
server {
    listen 80;
    server_name mordeczka.fleetman.com.pl;
    client_max_body_size 100m;

    error_page 502 503 504 /__fleetman_outage.html;
    proxy_intercept_errors on;
    location = /__fleetman_outage.html {
        alias /var/www/fleetman/outage.html;
        internal;
    }

    location ^~ /.well-known/acme-challenge/ {
        root /var/www/certbot;
        try_files $uri =404;
    }

    # Blazor Server: bez Upgrade/Connection strona sie wyrenderuje, ale
    # kliknięcia przestana odpowiadac (SignalR nie zestawi WebSocketa).
    location / {
        proxy_pass http://127.0.0.1:8087;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_buffering off;
        client_max_body_size 100m;
    }
}
