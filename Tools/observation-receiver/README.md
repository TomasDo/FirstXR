# Observation receiver fixture

The navigation workstation owns the production media receiver. This folder provides a small FFplay fixture for XREAL device testing:

```bash
chmod +x receive.sh
./receive.sh 5555
```

Configure `ObservationControl.receiver_host` to the workstation LAN IP and `receiver_port` to the same UDP port. Set either `xr_mirror_enabled`, `rgb_enabled`, or both. The XREAL encoder emits one fixed 1280×720 layout by default: left 640×720 is the actual XR left-eye render and right 640×720 is RGB; an unrequested pane is black.

The XREAL SDK decides the encoded payload. If FFplay requires SDP or an explicit demuxer on the target SDK/device build, record the codec, payload type and working FFmpeg arguments during the first hardware test and use them in the navigation software. This fixture intentionally does not claim a codec before that measurement.
