{{/* Chart name and version label. */}}
{{- define "booking.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{/* Labels shared by every object. */}}
{{- define "booking.commonLabels" -}}
helm.sh/chart: {{ include "booking.chart" . }}
app.kubernetes.io/part-of: booking
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
{{- end -}}

{{/* Selector labels for a component. Call with (dict "root" $ "name" $name). */}}
{{- define "booking.selectorLabels" -}}
app.kubernetes.io/name: {{ .name }}
app.kubernetes.io/instance: {{ .root.Release.Name }}
{{- end -}}

{{- define "booking.labels" -}}
{{ include "booking.commonLabels" .root }}
{{ include "booking.selectorLabels" . }}
app.kubernetes.io/component: {{ .component | default "service" }}
{{- end -}}

{{/* Service config merged over chart-wide defaults. Call with (dict "root" $ "svc" $svc); returns YAML. */}}
{{- define "booking.serviceConfig" -}}
{{- $merged := mergeOverwrite (deepCopy .root.Values.defaults) (deepCopy .svc) -}}
{{- toYaml $merged -}}
{{- end -}}

{{- define "booking.image" -}}
{{- $g := .root.Values.global.image -}}
{{- $repo := .svc.image.repository -}}
{{- if $g.registry }}{{ $repo = printf "%s/%s" (trimSuffix "/" $g.registry) $repo }}{{ end -}}
{{- printf "%s:%s" $repo (.svc.image.tag | default $g.tag | toString) -}}
{{- end -}}

{{- define "booking.configMapName" -}}{{ .name }}-config{{- end -}}

{{- define "booking.secretName" -}}
{{- .svc.existingSecret | default (printf "%s-secret" .name) -}}
{{- end -}}

{{/* Non-secret env for a service: computed JWT settings, then global.env, then service env. */}}
{{- define "booking.env" -}}
{{- $g := .root.Values.global -}}
{{- $env := dict "ASPNETCORE_ENVIRONMENT" $g.aspnetcoreEnvironment "Jwt__Authority" $g.publicUrl "Jwt__MetadataAddress" $g.jwtMetadataAddress -}}
{{- if eq .name "identity-service" }}{{ $_ := set $env "AuthOptions__IssuerUri" $g.publicUrl }}{{ end -}}
{{- $env = mergeOverwrite $env (deepCopy ($g.env | default dict)) (deepCopy (.svc.env | default dict)) -}}
{{- toYaml $env -}}
{{- end -}}
