{{/*
Single-replica dev infrastructure component. Call with
(dict "root" $ "name" "postgres" "cfg" $cfg "ports" (list (dict "name" "x" "port" 1)) "dataPath" "/data"
      "env" (list ...) "args" (list ...) "probe" (dict ...) "extraVolumes" (list) "extraMounts" (list)).
*/}}
{{- define "booking.infra" -}}
{{- $ctx := dict "root" .root "name" .name "component" "infrastructure" }}
{{- if and .dataPath .cfg.persistence .cfg.persistence.enabled }}
---
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: {{ .name }}-data
  labels:
    {{- include "booking.labels" $ctx | nindent 4 }}
spec:
  accessModes: [ReadWriteOnce]
  {{- with .cfg.persistence.storageClass }}
  storageClassName: {{ . }}
  {{- end }}
  resources:
    requests:
      storage: {{ .cfg.persistence.size }}
{{- end }}
---
apiVersion: v1
kind: Service
metadata:
  name: {{ .name }}
  labels:
    {{- include "booking.labels" $ctx | nindent 4 }}
spec:
  type: ClusterIP
  selector:
    {{- include "booking.selectorLabels" $ctx | nindent 4 }}
  ports:
    {{- range .ports }}
    - name: {{ .name }}
      port: {{ .port }}
      targetPort: {{ .name }}
    {{- end }}
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: {{ .name }}
  labels:
    {{- include "booking.labels" $ctx | nindent 4 }}
spec:
  replicas: 1
  strategy:
    type: Recreate
  selector:
    matchLabels:
      {{- include "booking.selectorLabels" $ctx | nindent 6 }}
  template:
    metadata:
      labels:
        {{- include "booking.labels" $ctx | nindent 8 }}
      {{- with .annotations }}
      annotations:
        {{- toYaml . | nindent 8 }}
      {{- end }}
    spec:
      # EventStoreDB rejects unknown EVENTSTORE_* options such as the injected EVENTSTORE_SERVICE_PORT.
      enableServiceLinks: false
      containers:
        - name: {{ .name }}
          image: {{ .cfg.image }}
          imagePullPolicy: IfNotPresent
          {{- with .args }}
          args:
            {{- toYaml . | nindent 12 }}
          {{- end }}
          {{- with .env }}
          env:
            {{- toYaml . | nindent 12 }}
          {{- end }}
          ports:
            {{- range .ports }}
            - name: {{ .name }}
              containerPort: {{ .port }}
            {{- end }}
          readinessProbe:
            {{- toYaml .probe | nindent 12 }}
            periodSeconds: 5
            timeoutSeconds: 5
            failureThreshold: 60
          resources:
            {{- toYaml .cfg.resources | nindent 12 }}
          {{- if or .dataPath .extraMounts }}
          volumeMounts:
            {{- if .dataPath }}
            - name: data
              mountPath: {{ .dataPath }}
            {{- end }}
            {{- with .extraMounts }}
            {{- toYaml . | nindent 12 }}
            {{- end }}
          {{- end }}
      {{- if or .dataPath .extraVolumes }}
      volumes:
        {{- if .dataPath }}
        - name: data
          {{- if and .cfg.persistence .cfg.persistence.enabled }}
          persistentVolumeClaim:
            claimName: {{ .name }}-data
          {{- else }}
          emptyDir: {}
          {{- end }}
        {{- end }}
        {{- with .extraVolumes }}
        {{- toYaml . | nindent 8 }}
        {{- end }}
      {{- end }}
{{- end -}}
