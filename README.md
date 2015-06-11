# Google Drive Site Deployment Client

This tool is to deploy static web site as a public website on google drive.
The client will synchronize the site to the one on the local drive, e.g.:
will create missing and changes files and create new ones

# Compilation

You will need to create your own "client_secret.json" file. Use google API
documentation on https://developers.google.com/drive/web/about-sdk

# Usage

    GDriveSiteDeployCli <source-path> [target-path]

    source-path   Path on local file system, where site is located
    target-path   Path on Google Drive, where files would be pushed to


