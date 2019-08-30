# deviceonline
Pings devices on a network, mini web server and windows service, to view status, configure service and setup email alerts.

Definately re-inventing a wheel, this pings devices on a network but more uniquely provides an easy to setup and use self contained
windows service and mini webserver / sqlite to both configure device pinging and view reports, aswell as setup email notifications.

You need .net framework 4 or later.
You need SQLite which has been provided.

Make sure you get the exe and copy the 'release\deviceonline' to the same folder.
Run cmd as admin, navigate to the release folder and use install_service.bat, to setup a windows service.
Designed to try and be enterprise grade with support for https, all passwords hashed and salted, etc smtp password with is windows password store encrypted.

Navigate to http(s)://localhost/deviceonline/ or http(s)//[servername]/deviceonline/
Username: admin, password: admin

You also of course have the c# source code to compile your own exe.
This is my first open source project on github, I hope I have done it right!

Version 0.9
Just enough to get everything working with devices, device groups, dashboard, settings with dashboard layout options, 
time periods to set notification times for peak off peak etc, view device and group uptime with basic pie charts and uptime percents.
Filter uptime results by day/week/month.

Whats missing? setting up https certificates, data validation, email combining (if 10 devices go down you get 10 emails at the minute)

Enjoy.
